using Amazon.Lambda.DynamoDBEvents;

namespace Pricing.Function.Modules.Prices.EventsIntegration.Publishers;

public sealed record PriceStreamImage(string ProductId, decimal NominalPrice, decimal Cost)
{
    public static PriceStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new PriceStreamImage(
            image.TryGetValue("ProductId", out var productId) ? productId.S : string.Empty,
            image.TryGetValue("NominalPrice", out var nominalPrice) && !string.IsNullOrEmpty(nominalPrice.N)
                ? decimal.Parse(nominalPrice.N, CultureInfo.InvariantCulture)
                : 0m,
            image.TryGetValue("Cost", out var cost) && !string.IsNullOrEmpty(cost.N)
                ? decimal.Parse(cost.N, CultureInfo.InvariantCulture)
                : 0m);
    }
}
