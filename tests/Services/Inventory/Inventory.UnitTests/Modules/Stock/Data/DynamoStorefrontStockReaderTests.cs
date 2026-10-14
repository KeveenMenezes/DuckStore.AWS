using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Inventory.Function.Modules.Stock.Data;

namespace Inventory.UnitTests.Modules.Stock.Data;

public class DynamoStorefrontStockReaderTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private DynamoStorefrontStockReader Reader => new(_dynamoDb.Object);

    private static Dictionary<string, AttributeValue> Balance(string warehouseId, int available, long? version) => new Dictionary<string, AttributeValue>
    {
        ["ProductId"] = new() { S = "p-1" },
        ["WarehouseId"] = new() { S = warehouseId },
        ["Available"] = new() { N = available.ToString(CultureInfo.InvariantCulture) },
    }.WithVersion(version);

    private static Dictionary<string, AttributeValue> Warehouse(string id, bool active, long? version, string type = "Warehouse") => new Dictionary<string, AttributeValue>
    {
        ["WarehouseId"] = new() { S = id },
        ["Type"] = new() { S = type },
        ["Active"] = new() { BOOL = active },
    }.WithVersion(version);

    private void Balances(params QueryResponse[] pages)
    {
        var setup = _dynamoDb.SetupSequence(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()));
        foreach (var page in pages) setup = setup.ReturnsAsync(page);
    }

    [Fact]
    public async Task TheTotal_FollowsThePages_AndCountsAMissingVersionAsZero()
    {
        Balances(
            new QueryResponse { Items = [Balance("a", 20, 3)], LastEvaluatedKey = new() { ["ProductId"] = new() { S = "p-1" } } },
            new QueryResponse { Items = [Balance("b", 50, null)] });
        _dynamoDb.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new() { ["inventory-warehouses"] = [Warehouse("a", true, 2), Warehouse("b", true, null)] },
            });

        var total = await Reader.TotalForAsync("p-1", default);

        Assert.Equal((70, 5L), (total.Available, total.Version));
        _dynamoDb.Verify(d => d.QueryAsync(It.Is<QueryRequest>(q => q.ConsistentRead == true), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AGuardItem_IsNeverTakenForAWarehouse()
    {
        Balances(new QueryResponse { Items = [Balance("a", 20, 1)] });
        _dynamoDb.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse { Responses = new() { ["inventory-warehouses"] = [Warehouse("a", true, 1, type: "CodeGuard")] } });

        Assert.Equal(0, (await Reader.TotalForAsync("p-1", default)).Available);
    }

    // SDK v4 leaves absent maps null; unprocessed keys are asked for again.
    [Fact]
    public async Task UnprocessedKeys_AreRetried_AndNullMapsAreEmpty()
    {
        Balances(new QueryResponse { Items = [Balance("a", 20, 1), Balance("b", 5, 1)] });
        _dynamoDb.SetupSequence(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new() { ["inventory-warehouses"] = [Warehouse("a", true, 1)] },
                UnprocessedKeys = new() { ["inventory-warehouses"] = new KeysAndAttributes { Keys = [new() { ["WarehouseId"] = new() { S = "b" } }] } },
            })
            .ReturnsAsync(new BatchGetItemResponse { Responses = new() { ["inventory-warehouses"] = [Warehouse("b", true, 1)] }, UnprocessedKeys = null });

        var total = await Reader.TotalForAsync("p-1", default);

        Assert.Equal((25, 4L), (total.Available, total.Version));
    }

    [Fact]
    public async Task MoreThanAHundredWarehouses_AreReadInChunksOfAHundred()
    {
        Balances(new QueryResponse { Items = [.. Enumerable.Range(0, 150).Select(i => Balance($"w{i}", 1, 1))] });
        var sizes = new List<int>();
        _dynamoDb.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BatchGetItemRequest, CancellationToken>((request, _) => sizes.Add(request.RequestItems["inventory-warehouses"].Keys.Count))
            .ReturnsAsync(new BatchGetItemResponse());

        await Reader.TotalForAsync("p-1", default);

        Assert.Equal([100, 50], sizes);
    }

    [Fact]
    public async Task TheProductsOfAWarehouse_FollowTheGsiPages()
    {
        Balances(
            new QueryResponse { Items = [new() { ["ProductId"] = new() { S = "p-1" } }], LastEvaluatedKey = new() { ["WarehouseId"] = new() { S = "w" } } },
            new QueryResponse { Items = [new() { ["ProductId"] = new() { S = "p-2" } }] });

        Assert.Equal(["p-1", "p-2"], await Reader.ProductsInWarehouseAsync("w", default));
    }
}

internal static class StorefrontItemExtensions
{
    public static Dictionary<string, AttributeValue> WithVersion(this Dictionary<string, AttributeValue> item, long? version)
    {
        if (version is { } v) item["Version"] = new AttributeValue { N = v.ToString(CultureInfo.InvariantCulture) };
        return item;
    }
}
