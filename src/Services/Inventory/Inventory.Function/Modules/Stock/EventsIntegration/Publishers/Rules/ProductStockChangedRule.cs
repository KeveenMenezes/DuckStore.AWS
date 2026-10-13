namespace Inventory.Function.Modules.Stock.EventsIntegration.Publishers.Rules;

// A balance moved, so the product's storefront total may have (ADR-0052 §8). The total is recomputed
// from the current state, not from the image: two records of one product in a batch publish the same
// total and AsOf, and the consumer drops the repeat.
public sealed class ProductStockChangedRule(IStorefrontStockReader reader) : IStreamRule<StockStreamImage>
{
    public bool Match(StreamContext<StockStreamImage> context) =>
        context.EventName is "INSERT" or "MODIFY" && !string.IsNullOrEmpty(context.New?.ProductId);

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<StockStreamImage> context, CancellationToken cancellationToken = default)
    {
        var productId = context.New!.ProductId;
        var total = await reader.TotalForAsync(productId, cancellationToken);
        return new PublishInstruction(
            nameof(ProductStockChangedEvent),
            new ProductStockChangedEvent { ProductId = productId, Available = total.Available, AsOf = total.AsOf });
    }
}
