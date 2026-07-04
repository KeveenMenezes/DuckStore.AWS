namespace Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers;

// The subset of a persisted cart row the publisher rule reasons about, projected from a
// DynamoDB Streams image. Only the new image is ever read (see CheckoutedRule).
public sealed record ShoppingCartStreamImage(string OwnerId, string Type, string? CheckoutData)
{
    public static ShoppingCartStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new ShoppingCartStreamImage(
            image.TryGetValue("OwnerId", out var ownerId) ? ownerId.S : string.Empty,
            image.TryGetValue("Type", out var type) ? type.S : string.Empty,
            image.TryGetValue("CheckoutData", out var checkoutData) ? checkoutData.S : null);
    }
}
