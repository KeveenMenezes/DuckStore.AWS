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

    // The checkout saga inserts a CHECKOUT# record instead of marking the cart row (ADR-0050 §4).
    // Both shapes publish while the old pipeline is still rolling out; each checkout writes only one.
    [Fact]
    public void Match_ReturnsTrue_ForInsertOfACheckoutRecordWithData() =>
        Assert.True(_rule.Match(Context("INSERT")));

    [Fact]
    public void Match_ReturnsFalse_ForRemoveEvents() =>
        Assert.False(_rule.Match(Context("REMOVE")));

    [Fact]
    public void Match_ReturnsFalse_ForInsertOfAnOrdinaryCart() =>
        Assert.False(_rule.Match(Context("INSERT", newType: string.Empty, checkoutData: null)));

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
