namespace CatalogView.Function;

// Triggered by EventBridge. Consumes ProductDeletedEvent and removes the product from the
// catalogview-products projection.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task ProductDeletedConsumer(
        EventBridgeEvent<ProductDeletedEvent> evt,
        [FromServices] ProductDeletedHandler handler)
    {
        await handler.HandleAsync(evt.Detail);
    }
}
