using System.Text.Json;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;
using CatalogView.Function.Modules.Products.EventsIntegration.Consumers.CatalogSync;

namespace CatalogView.UnitTests.Products;

public class CatalogSyncDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_RoutesToTheStrategyThatCanHandleTheDetailType()
    {
        var matching = new Mock<ICatalogSyncStrategy>();
        matching.Setup(s => s.CanHandle(nameof(ProductDeletedEvent))).Returns(true);

        var other = new Mock<ICatalogSyncStrategy>();
        other.Setup(s => s.CanHandle(It.IsAny<string>())).Returns(false);

        var dispatcher = new CatalogSyncDispatcher([other.Object, matching.Object]);

        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = nameof(ProductDeletedEvent),
            Detail = JsonSerializer.SerializeToElement(new ProductDeletedEvent { ProductId = "product-1" })
        };

        await dispatcher.DispatchAsync(evt);

        matching.Verify(
            s => s.HandleAsync("event-1", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        other.Verify(
            s => s.HandleAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenNoStrategyRegisteredForDetailType()
    {
        var dispatcher = new CatalogSyncDispatcher([]);

        var evt = new EventBridgeEvent<JsonElement>
        {
            Id = "event-1",
            DetailType = "SomeUnknownEvent",
            Detail = JsonSerializer.SerializeToElement(new { })
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(evt));
    }
}
