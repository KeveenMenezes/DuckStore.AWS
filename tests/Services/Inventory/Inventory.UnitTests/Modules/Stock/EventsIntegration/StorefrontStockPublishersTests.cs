using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.EventsIntegration.Publishers;
using Inventory.Function.Modules.Stock.EventsIntegration.Publishers.Rules;
using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;

namespace Inventory.UnitTests.Modules.Stock.EventsIntegration;

public class StorefrontStockPublishersTests
{
    private readonly Mock<IStorefrontStockReader> _reader = new();
    private readonly Mock<IEventPublisher> _publisher = new();
    private IReadOnlyList<PublishInstruction>? _published;

    public StorefrontStockPublishersTests()
    {
        _reader.Setup(r => r.TotalForAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string productId, CancellationToken _) => new StorefrontTotal(productId.Length, "2026-10-13T12:00:00.000Z"));
        _publisher.Setup(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<PublishInstruction>, CancellationToken>((instructions, _) => _published = instructions)
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData("INSERT", true)]
    [InlineData("MODIFY", true)]
    [InlineData("REMOVE", false)]
    public void TheStockRule_MatchesEveryBalanceWrite(string eventName, bool expected)
    {
        var rule = new ProductStockChangedRule(_reader.Object);
        var image = new StockStreamImage("p-1", "w-1");

        Assert.Equal(expected, rule.Match(new StreamContext<StockStreamImage>(eventName, image, eventName == "REMOVE" ? null : image)));
    }

    [Fact]
    public async Task TheStockRule_PublishesTheRecomputedTotal()
    {
        _reader.Setup(r => r.TotalForAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(new StorefrontTotal(70, "2026-10-13T12:00:00.000Z"));
        var rule = new ProductStockChangedRule(_reader.Object);

        var instruction = await rule.BuildAsync(new StreamContext<StockStreamImage>("MODIFY", null, new StockStreamImage("p-1", "w-1")));

        Assert.Equal(nameof(ProductStockChangedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductStockChangedEvent>(instruction.Payload);
        Assert.Equal(("p-1", 70, "2026-10-13T12:00:00.000Z"), (payload.ProductId, payload.Available, payload.AsOf));
    }

    private static StreamContext<WarehouseStreamImage> Warehouse(string eventName, bool? wasActive, bool isActive, string type = "Warehouse") =>
        new(eventName,
            wasActive is null ? null : new WarehouseStreamImage("w-1", type, wasActive.Value),
            new WarehouseStreamImage("w-1", type, isActive));

    [Fact]
    public async Task Deactivating_PublishesOneEventPerProductInTheWarehouse_InOneBatchCall()
    {
        _reader.Setup(r => r.ProductsInWarehouseAsync("w-1", It.IsAny<CancellationToken>())).ReturnsAsync(["p-1", "p-22", "p-333"]);
        var handler = new WarehouseActivationPublisher(_reader.Object, _publisher.Object);

        await handler.PublishAsync([Warehouse("MODIFY", wasActive: true, isActive: false)], default);

        Assert.Equal(["p-1", "p-22", "p-333"], _published!.Select(i => ((ProductStockChangedEvent)i.Payload).ProductId));
        Assert.All(_published, i => Assert.Equal(nameof(ProductStockChangedEvent), i.DetailType));
        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("MODIFY", true, true, "Warehouse")]      // an edit that leaves Active alone
    [InlineData("INSERT", null, true, "Warehouse")]      // a new warehouse holds no stock yet
    [InlineData("MODIFY", true, false, "CodeGuard")]     // never a guard
    public async Task OnlyAChangeOfActive_OnAWarehouse_PublishesAnything(string eventName, bool? wasActive, bool isActive, string type)
    {
        var handler = new WarehouseActivationPublisher(_reader.Object, _publisher.Object);

        await handler.PublishAsync([Warehouse(eventName, wasActive, isActive, type)], default);

        _reader.Verify(r => r.ProductsInWarehouseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWarehouseWithNoStock_PublishesNothing()
    {
        _reader.Setup(r => r.ProductsInWarehouseAsync("w-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var handler = new WarehouseActivationPublisher(_reader.Object, _publisher.Object);

        await handler.PublishAsync([Warehouse("MODIFY", wasActive: false, isActive: true)], default);

        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
