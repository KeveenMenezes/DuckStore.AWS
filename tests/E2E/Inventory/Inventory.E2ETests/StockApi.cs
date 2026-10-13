using System.Text.Json.Nodes;

namespace Inventory.E2ETests;

// The stock GraphQL operations (ADR-0052 §7), sent straight to AppSync as a signed-in staff member.
public sealed class StockApi(ManagementSession session)
{
    private const string MovementFields =
        "id productId type quantity balanceAfter warehouseId warehouseCode invoiceNumber supplier reason note transferId counterpartWarehouseId createdAt createdBy";

    // A real Catalog product: the receipt and the increase check it exists.
    public async Task<(string Id, string Name)> AnyProductAsync()
    {
        var response = await session.GraphQLAsync("{ products(pageSize: 1) { items { id name } } }");
        var product = response.Data?["products"]?["items"]?[0]
            ?? throw new InvalidOperationException($"No product in Catalog: {response}");
        return ((string)product["id"]!, (string)product["name"]!);
    }

    public Task<GraphQLResponse> ReceiveAsync(string productId, string warehouseId, int quantity,
        string? invoice = "NF-E2E", string? key = null, string? supplier = null) =>
        session.GraphQLAsync(
            $"mutation($input: RecordStockReceiptInput!) {{ recordStockReceipt(input: $input) {{ movements {{ {MovementFields} }} }} }}",
            new { input = new { idempotencyKey = key ?? Guid.NewGuid().ToString(), productId, warehouseId, quantity, invoiceNumber = invoice, supplier } });

    public Task<GraphQLResponse> AdjustAsync(string productId, string warehouseId, string direction, int quantity,
        string reason = "Count", string? note = null, string? key = null) =>
        session.GraphQLAsync(
            $"mutation($input: RecordStockAdjustmentInput!) {{ recordStockAdjustment(input: $input) {{ movements {{ {MovementFields} }} }} }}",
            new { input = new { idempotencyKey = key ?? Guid.NewGuid().ToString(), productId, warehouseId, direction, quantity, reason, note } });

    public Task<GraphQLResponse> TransferAsync(string productId, string from, string to, int quantity, string? key = null) =>
        session.GraphQLAsync(
            $"mutation($input: TransferStockInput!) {{ transferStock(input: $input) {{ movements {{ {MovementFields} }} }} }}",
            new { input = new { idempotencyKey = key ?? Guid.NewGuid().ToString(), productId, fromWarehouseId = from, toWarehouseId = to, quantity } });

    public Task<GraphQLResponse> StockByProductAsync(string productId) => session.GraphQLAsync(
        "query($productId: ID!) { stockByProduct(productId: $productId) { productId totalAvailable warehouses { warehouseId warehouseCode available reserved } } }",
        new { productId });

    public Task<GraphQLResponse> StockByWarehouseAsync(string warehouseId, int? pageSize = null) => session.GraphQLAsync(
        "query($warehouseId: ID!, $pageSize: Int) { stockByWarehouse(warehouseId: $warehouseId, pageSize: $pageSize) { items { productId productName available reserved } nextToken } }",
        new { warehouseId, pageSize });

    public Task<GraphQLResponse> MovementsByProductAsync(string productId, int? pageSize = null, string? nextToken = null) => session.GraphQLAsync(
        $"query($productId: ID!, $pageSize: Int, $nextToken: String) {{ stockMovementsByProduct(productId: $productId, pageSize: $pageSize, nextToken: $nextToken) {{ items {{ {MovementFields} }} nextToken }} }}",
        new { productId, pageSize, nextToken });

    public Task<GraphQLResponse> MovementsByWarehouseAsync(string warehouseId, int? pageSize = null) => session.GraphQLAsync(
        $"query($warehouseId: ID!, $pageSize: Int) {{ stockMovementsByWarehouse(warehouseId: $warehouseId, pageSize: $pageSize) {{ items {{ productName {MovementFields} }} nextToken }} }}",
        new { warehouseId, pageSize });

    // A warehouse's available units of the product, read through stockByProduct.
    public async Task<int?> AvailableAsync(string productId, string warehouseId)
    {
        var stock = await StockByProductAsync(productId);
        var row = stock.Data!["stockByProduct"]!["warehouses"]!.AsArray()
            .FirstOrDefault(w => (string?)w!["warehouseId"] == warehouseId);
        return row is null ? null : (int)row["available"]!;
    }

    // Σ quantity over the warehouse's whole history: the ledger invariant of ADR-0052 §7.
    public async Task<int> LedgerSumAsync(string productId, string warehouseId)
    {
        var sum = 0;
        string? token = null;
        do
        {
            var page = await MovementsByProductAsync(productId, 100, token);
            var data = page.Data!["stockMovementsByProduct"]!;
            sum += data["items"]!.AsArray().Where(m => (string?)m!["warehouseId"] == warehouseId).Sum(m => (int)m!["quantity"]!);
            token = (string?)data["nextToken"];
        }
        while (token is not null);
        return sum;
    }

    public static JsonArray MovementsOf(GraphQLResponse response, string field) =>
        response.Data?[field]?["movements"]?.AsArray() ?? throw new InvalidOperationException(response.ToString());
}
