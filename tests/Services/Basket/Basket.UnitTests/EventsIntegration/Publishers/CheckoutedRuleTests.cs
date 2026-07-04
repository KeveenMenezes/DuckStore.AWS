using System.Text.Json;
using Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers;
using Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers.Rules;
using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;

namespace Basket.UnitTests.EventsIntegration.Publishers;

public class CheckoutedRuleTests
{
    private readonly CheckoutedRule _rule = new();

    private static StreamContext<ShoppingCartStreamImage> Context(
        string eventName, string? newType = "Checkout", string? checkoutData = "{}") =>
        new(eventName, null,
            newType is null ? null : new ShoppingCartStreamImage("USER#testuser", newType, checkoutData));

    [Fact]
    public void Match_ReturnsTrue_ForModifyOfCheckoutWithData() =>
        Assert.True(_rule.Match(Context("MODIFY")));

    [Theory]
    [InlineData("INSERT")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonModifyEvents(string eventName) =>
        Assert.False(_rule.Match(Context(eventName)));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsNotCheckout() =>
        Assert.False(_rule.Match(Context("MODIFY", newType: "Active")));

    [Fact]
    public void Match_ReturnsFalse_WhenCheckoutDataIsMissing() =>
        Assert.False(_rule.Match(Context("MODIFY", checkoutData: null)));

    [Fact]
    public void Match_ReturnsFalse_WhenNewImageIsMissing() =>
        Assert.False(_rule.Match(Context("MODIFY", newType: null)));

    [Fact]
    public async Task BuildAsync_ReturnsBasketCheckoutInstruction_FromEmbeddedCheckoutData()
    {
        var checkoutEvent = new BasketCheckoutEvent { OwnerId = "USER#testuser", TotalPrice = 100.0m };
        var checkoutData = JsonSerializer.Serialize(checkoutEvent);

        var instruction = await _rule.BuildAsync(Context("MODIFY", checkoutData: checkoutData));

        Assert.Equal(nameof(BasketCheckoutEvent), instruction.DetailType);
        var payload = Assert.IsType<BasketCheckoutEvent>(instruction.Payload);
        Assert.Equal("USER#testuser", payload.OwnerId);
        Assert.Equal(100.0m, payload.TotalPrice);
    }
}
