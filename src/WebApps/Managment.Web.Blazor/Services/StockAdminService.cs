namespace Managment.Web.Blazor.Services;

/// <summary>
/// Typed wrapper over the stock GraphQL operations (ADR-0052 §7). Operation shapes mirror
/// graphql/schema.graphql. Every operation is StockKeeper/Admin only — enforced server-side by the
/// resolvers; a refused movement surfaces as <see cref="GraphQLException"/> with ErrorType
/// (BadRequest, ProductNotFound, WarehouseNotFound, WarehouseInactive, InsufficientStock,
/// IdempotencyKeyReused, TransactionConflict), ErrorField and, for InsufficientStock, ErrorAvailable.
/// </summary>
public sealed class StockAdminService(GraphQLClient gql)
{
    private const string MovementFields =
        """
        id
        productId
        productName
        type
        quantity
        balanceAfter
        warehouseId
        warehouseCode
        invoiceNumber
        supplier
        reason
        note
        transferId
        counterpartWarehouseId
        createdAt
        createdBy
        """;

    private const string StockByProductQuery =
        """
        query StockByProduct($productId: ID!) {
          stockByProduct(productId: $productId) {
            productId
            totalAvailable
            warehouses { warehouseId warehouseCode available reserved }
          }
        }
        """;

    private const string StockByWarehouseQuery =
        """
        query StockByWarehouse($warehouseId: ID!, $pageSize: Int, $nextToken: String) {
          stockByWarehouse(warehouseId: $warehouseId, pageSize: $pageSize, nextToken: $nextToken) {
            items { productId productName available reserved }
            nextToken
          }
        }
        """;

    private const string MovementsByProductQuery =
        $$"""
        query StockMovementsByProduct($productId: ID!, $pageSize: Int, $nextToken: String) {
          stockMovementsByProduct(productId: $productId, pageSize: $pageSize, nextToken: $nextToken) {
            items { {{MovementFields}} }
            nextToken
          }
        }
        """;

    private const string MovementsByWarehouseQuery =
        $$"""
        query StockMovementsByWarehouse($warehouseId: ID!, $pageSize: Int, $nextToken: String) {
          stockMovementsByWarehouse(warehouseId: $warehouseId, pageSize: $pageSize, nextToken: $nextToken) {
            items { {{MovementFields}} }
            nextToken
          }
        }
        """;

    private const string RecordStockReceiptMutation =
        $$"""
        mutation RecordStockReceipt($input: RecordStockReceiptInput!) {
          recordStockReceipt(input: $input) { movements { {{MovementFields}} } }
        }
        """;

    private const string RecordStockAdjustmentMutation =
        $$"""
        mutation RecordStockAdjustment($input: RecordStockAdjustmentInput!) {
          recordStockAdjustment(input: $input) { movements { {{MovementFields}} } }
        }
        """;

    private const string TransferStockMutation =
        $$"""
        mutation TransferStock($input: TransferStockInput!) {
          transferStock(input: $input) { movements { {{MovementFields}} } }
        }
        """;

    // One row per warehouse holding the product, inactive ones included.
    public async Task<ProductStockInfo> GetStockByProductAsync(string productId)
    {
        var data = await gql.SendAsync<StockByProductData>(StockByProductQuery, new { productId });
        return data.StockByProduct;
    }

    public async Task<WarehouseProductStockPage> GetStockByWarehouseAsync(
        string warehouseId, int pageSize = 20, string? nextToken = null)
    {
        var data = await gql.SendAsync<StockByWarehouseData>(
            StockByWarehouseQuery, new { warehouseId, pageSize, nextToken });
        return data.StockByWarehouse;
    }

    // Newest first.
    public async Task<StockMovementPage> GetMovementsByProductAsync(
        string productId, int pageSize = 20, string? nextToken = null)
    {
        var data = await gql.SendAsync<MovementsByProductData>(
            MovementsByProductQuery, new { productId, pageSize, nextToken });
        return data.StockMovementsByProduct;
    }

    // Newest first.
    public async Task<StockMovementPage> GetMovementsByWarehouseAsync(
        string warehouseId, int pageSize = 20, string? nextToken = null)
    {
        var data = await gql.SendAsync<MovementsByWarehouseData>(
            MovementsByWarehouseQuery, new { warehouseId, pageSize, nextToken });
        return data.StockMovementsByWarehouse;
    }

    public async Task<StockMovementResult> RecordReceiptAsync(string productId, StockReceiptFormModel model)
    {
        var input = new
        {
            idempotencyKey = model.IdempotencyKey,
            productId,
            warehouseId = model.WarehouseId,
            quantity = model.Quantity,
            invoiceNumber = model.InvoiceNumber.Trim(),
            supplier = Optional(model.Supplier),
            note = Optional(model.Note),
        };
        var data = await gql.SendAsync<RecordStockReceiptData>(RecordStockReceiptMutation, new { input });
        return data.RecordStockReceipt;
    }

    public async Task<StockMovementResult> RecordAdjustmentAsync(string productId, StockAdjustmentFormModel model)
    {
        var input = new
        {
            idempotencyKey = model.IdempotencyKey,
            productId,
            warehouseId = model.WarehouseId,
            direction = model.Direction,
            quantity = model.Quantity,
            reason = model.Reason,
            note = Optional(model.Note),
        };
        var data = await gql.SendAsync<RecordStockAdjustmentData>(RecordStockAdjustmentMutation, new { input });
        return data.RecordStockAdjustment;
    }

    public async Task<StockMovementResult> TransferAsync(string productId, StockTransferFormModel model)
    {
        var input = new
        {
            idempotencyKey = model.IdempotencyKey,
            productId,
            fromWarehouseId = model.FromWarehouseId,
            toWarehouseId = model.ToWarehouseId,
            quantity = model.Quantity,
            note = Optional(model.Note),
        };
        var data = await gql.SendAsync<TransferStockData>(TransferStockMutation, new { input });
        return data.TransferStock;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record StockByProductData(ProductStockInfo StockByProduct);

    private sealed record StockByWarehouseData(WarehouseProductStockPage StockByWarehouse);

    private sealed record MovementsByProductData(StockMovementPage StockMovementsByProduct);

    private sealed record MovementsByWarehouseData(StockMovementPage StockMovementsByWarehouse);

    private sealed record RecordStockReceiptData(StockMovementResult RecordStockReceipt);

    private sealed record RecordStockAdjustmentData(StockMovementResult RecordStockAdjustment);

    private sealed record TransferStockData(StockMovementResult TransferStock);
}
