using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.EventsIntegration.Publishers;
using Inventory.Function.Modules.Stock.EventsIntegration.Publishers.Rules;
using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;
using Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers.Rules;

namespace Inventory.UnitTests.Modules.Stock.EventsIntegration;

public class StorefrontStockPublishersTests
{
    private readonly Mock<IStorefrontStockReader> _reader = new();
    private readonly Mock<IEventPublisher> _publisher = new();
    private IReadOnlyList<PublishInstruction>? _published;

    public StorefrontStockPublishersTests()
    {
        _reader.Setup(r => r.TotalForAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string productId, CancellationToken _) => new StorefrontTotal(productId.Length, productId.Length * 10));
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
        _reader.Setup(r => r.TotalForAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(new StorefrontTotal(70, 12));
        var rule = new ProductStockChangedRule(_reader.Object);

        var instruction = await rule.BuildAsync(new StreamContext<StockStreamImage>("MODIFY", null, new StockStreamImage("p-1", "w-1")));

        Assert.Equal(nameof(ProductStockChangedEvent), instruction.DetailType);
        var payload = Assert.IsType<ProductStockChangedEvent>(instruction.Payload);
        Assert.Equal(("p-1", 70, 12L), (payload.ProductId, payload.Available, payload.Version));
    }

    private StreamRuleDispatcher<WarehouseStreamImage> WarehouseDispatcher() =>
        new([], _publisher.Object, [new WarehouseActivationRule(_reader.Object)]);

    private static StreamContext<WarehouseStreamImage> Warehouse(string eventName, bool? wasActive, bool isActive, string type = "Warehouse") =>
        new(eventName,
            wasActive is null ? null : new WarehouseStreamImage("w-1", type, wasActive.Value),
            new WarehouseStreamImage("w-1", type, isActive));

    [Fact]
    public async Task Deactivating_PublishesOneEventPerProductInTheWarehouse_InOneBatchCall()
    {
        _reader.Setup(r => r.ProductsInWarehouseAsync("w-1", It.IsAny<CancellationToken>())).ReturnsAsync(["p-1", "p-22", "p-333"]);
        await WarehouseDispatcher().DispatchAsync([Warehouse("MODIFY", wasActive: true, isActive: false)]);

        Assert.Equal(["p-1", "p-22", "p-333"], _published!.Select(i => ((ProductStockChangedEvent)i.Payload).ProductId));
        Assert.All(_published, i => Assert.Equal(nameof(ProductStockChangedEvent), i.DetailType));
        Assert.Equal([30L, 40L, 50L], _published.Select(i => ((ProductStockChangedEvent)i.Payload).Version));
        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("MODIFY", true, true, "Warehouse")]      // an edit that leaves Active alone
    [InlineData("INSERT", null, true, "Warehouse")]      // a new warehouse holds no stock yet
    [InlineData("MODIFY", true, false, "CodeGuard")]     // never a guard
    public async Task OnlyAChangeOfActive_OnAWarehouse_PublishesAnything(string eventName, bool? wasActive, bool isActive, string type)
    {
        await WarehouseDispatcher().DispatchAsync([Warehouse(eventName, wasActive, isActive, type)]);

        _reader.Verify(r => r.ProductsInWarehouseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWarehouseWithNoStock_PublishesNothing()
    {
        _reader.Setup(r => r.ProductsInWarehouseAsync("w-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await WarehouseDispatcher().DispatchAsync([Warehouse("MODIFY", wasActive: false, isActive: true)]);

        _publisher.Verify(p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
