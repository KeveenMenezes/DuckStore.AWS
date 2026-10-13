namespace Inventory.Function.Modules.Warehouses.EventsIntegration.Publishers;

// Activating or deactivating a warehouse changes the storefront total of every product it holds
// (ADR-0052 §8). One record becomes one event per product, which a StreamRuleDispatcher rule (one
// instruction per record) cannot express, so this builds the instructions and publishes them in
// batches. Guards, new warehouses and edits that leave Active alone publish nothing.
public sealed class WarehouseActivationPublisher(IStorefrontStockReader reader, IEventPublisher publisher)
{
    public static bool ChangesTheTotal(StreamContext<WarehouseStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.New is { Type: WarehousesSchema.WarehouseType } now
        && context.Old is { } before
        && before.Active != now.Active;

    public async Task PublishAsync(IReadOnlyList<StreamContext<WarehouseStreamImage>> contexts, CancellationToken cancellationToken)
    {
        var instructions = new List<PublishInstruction>();
        foreach (var context in contexts.Where(ChangesTheTotal))
        {
            foreach (var productId in await reader.ProductsInWarehouseAsync(context.New!.WarehouseId, cancellationToken))
            {
                var total = await reader.TotalForAsync(productId, cancellationToken);
                instructions.Add(new PublishInstruction(
                    nameof(ProductStockChangedEvent),
                    new ProductStockChangedEvent { ProductId = productId, Available = total.Available, AsOf = total.AsOf }));
            }
        }

        if (instructions.Count > 0)
            await publisher.PublishManyAsync(instructions, cancellationToken);
    }
}
