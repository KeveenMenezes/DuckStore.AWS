namespace Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers.Rules;

// Activating or deactivating a warehouse changes the storefront total of every product it holds
// (ADR-0052 §8): one record becomes one event per product, so this is a fan-out rule. Guards, new
// warehouses and edits that leave Active alone match nothing.
public sealed class WarehouseActivationRule(IStorefrontStockReader reader) : IStreamFanOutRule<WarehouseStreamImage>
{
    public bool Match(StreamContext<WarehouseStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.New is { Type: WarehousesSchema.WarehouseType } now
        && context.Old is { } before
        && before.Active != now.Active;

    public async Task<IReadOnlyList<PublishInstruction>> BuildAllAsync(
        StreamContext<WarehouseStreamImage> context, CancellationToken cancellationToken = default)
    {
        var instructions = new List<PublishInstruction>();
        foreach (var productId in await reader.ProductsInWarehouseAsync(context.New!.WarehouseId, cancellationToken))
        {
            var total = await reader.TotalForAsync(productId, cancellationToken);
            instructions.Add(new PublishInstruction(
                nameof(ProductStockChangedEvent),
                new ProductStockChangedEvent { ProductId = productId, Available = total.Available, Version = total.Version }));
        }

        return instructions;
    }
}
