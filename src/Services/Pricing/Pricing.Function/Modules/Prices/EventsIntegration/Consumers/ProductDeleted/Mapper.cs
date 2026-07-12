namespace Pricing.Function.Modules.Prices.EventsIntegration.Consumers.ProductDeleted;

public static class ProductDeletedMapper
{
    public static Guid ToProductId(ProductDeletedEvent evt) => Guid.Parse(evt.ProductId);
}
