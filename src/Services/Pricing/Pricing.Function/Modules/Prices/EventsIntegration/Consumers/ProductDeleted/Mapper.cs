namespace Pricing.Function.Modules.Prices.EventsIntegration.Consumers.CatalogProductRemoved;

public static class CatalogProductRemovedMapper
{
    public static Guid ToProductId(CatalogUpdatedEvent evt) => Guid.Parse(evt.ProductId);
}
