using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.Domain.Enums;
using Inventory.Function.Modules.Warehouses.Domain;

namespace Inventory.UnitTests.Modules.Stock.Data;

public class DynamoStockRepositoryTests
{
    private const string ProductId = "p-1";
    private static readonly WarehouseSnapshot Alpha = new("w-alpha", "ALPHA", true);
    private static readonly DateTime At = new(2026, 10, 13, 12, 0, 0, 5, DateTimeKind.Utc);
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoStockRepository _repository;
    private TransactWriteItemsRequest? _written;

    public DynamoStockRepositoryTests()
    {
        _repository = new DynamoStockRepository(_dynamoDb.Object, new FixedTime(At));
        _dynamoDb.Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => _written = request)
            .ReturnsAsync(new TransactWriteItemsResponse());
    }

    private static MovementContext Context => new(Guid.Parse("11111111-2222-4333-8444-555555555555"), "sub-1", At, "hash-1");

    [Fact]
    public async Task Save_AFirstReceipt_UpdatesTheNewBalance_PutsTheRow_AndChecksTheWarehouseIsActive()
    {
        var balance = StockBalance.Empty(ProductId, Alpha);
        var movements = balance.Receive(20, "NF-1", null, null, Context).Movements;

        var outcome = await _repository.SaveAsync(
            new StockWrite([balance], movements, [new WarehouseCondition("w-alpha", MustBeActive: true)]), default);

        Assert.Equal(SaveOutcome.Saved, outcome);
        Assert.Collection(_written!.TransactItems,
            update =>
            {
                Assert.Equal("inventory-stock", update.Update.TableName);
                Assert.Equal("attribute_not_exists(ProductId)", update.Update.ConditionExpression);
                Assert.Equal("20", update.Update.ExpressionAttributeValues[":available"].N);
                Assert.Equal("ALPHA", update.Update.ExpressionAttributeValues[":code"].S);
                Assert.False(update.Update.ExpressionAttributeValues.ContainsKey(":read"));
                Assert.False(update.Update.ExpressionAttributeValues.ContainsKey(":reserved"));
            },
            put =>
            {
                Assert.Equal("inventory-movements", put.Put.TableName);
                Assert.Equal("attribute_not_exists(MovementId)", put.Put.ConditionExpression);
                Assert.Equal("11111111-2222-4333-8444-555555555555", put.Put.Item["MovementId"].S);
                Assert.Equal("Receipt", put.Put.Item["Type"].S);
                Assert.Equal("NF-1", put.Put.Item["InvoiceNumber"].S);
                Assert.Equal("2026-10-13T12:00:00.005Z", put.Put.Item["CreatedAt"].S);
                Assert.False(put.Put.Item.ContainsKey("Reason"));
            },
            check =>
            {
                Assert.Equal("inventory-warehouses", check.ConditionCheck.TableName);
                Assert.Equal("#type = :warehouse AND Active = :true", check.ConditionCheck.ConditionExpression);
            });
    }

    [Fact]
    public async Task Save_AnExistingBalance_WritesAgainstTheValueRead()
    {
        var balance = StockBalance.Load(ProductId, Alpha, available: 7, reserved: 0);
        var movements = balance.Adjust(StockAdjustmentDirection.Decrease, 2, StockAdjustmentReason.Loss, null, Context).Movements;

        await _repository.SaveAsync(new StockWrite([balance], movements, [new WarehouseCondition("w-alpha", false)]), default);

        var update = _written!.TransactItems[0].Update;
        Assert.Equal("Available = :read AND Reserved = :reserved", update.ConditionExpression);
        Assert.Equal("7", update.ExpressionAttributeValues[":read"].N);
        Assert.Equal("0", update.ExpressionAttributeValues[":reserved"].N);
        Assert.Equal("5", update.ExpressionAttributeValues[":available"].N);
        Assert.Equal("#type = :warehouse", _written.TransactItems[2].ConditionCheck.ConditionExpression);
        Assert.False(_written.TransactItems[2].ConditionCheck.ExpressionAttributeValues.ContainsKey(":true"));
    }

    [Theory]
    // 1 balance, 1 movement, 1 warehouse check, in that order.
    [InlineData(new[] { "None", "ConditionalCheckFailed", "None" }, SaveOutcome.MovementExists)]
    [InlineData(new[] { "ConditionalCheckFailed", "ConditionalCheckFailed", "None" }, SaveOutcome.MovementExists)]
    [InlineData(new[] { "None", "None", "ConditionalCheckFailed" }, SaveOutcome.WarehouseChanged)]
    [InlineData(new[] { "ConditionalCheckFailed", "None", "None" }, SaveOutcome.BalanceChanged)]
    [InlineData(new[] { "TransactionConflict", "None", "None" }, SaveOutcome.BalanceChanged)]
    public void OutcomeOf_ReadsTheCancellationReasonsByPosition(string[] codes, SaveOutcome expected)
    {
        var reasons = codes.Select(code => new CancellationReason { Code = code }).ToList();

        Assert.Equal(expected, DynamoStockRepository.OutcomeOf(reasons, balances: 1, movements: 1));
    }

    [Theory]
    // A transfer: 2 balances, 2 movements, 2 warehouse checks.
    [InlineData(3, SaveOutcome.MovementExists)]
    [InlineData(5, SaveOutcome.WarehouseChanged)]
    [InlineData(1, SaveOutcome.BalanceChanged)]
    public void OutcomeOf_ATransferLayout_IsReadAtItsOffsets(int failedIndex, SaveOutcome expected)
    {
        var reasons = Enumerable.Range(0, 6)
            .Select(i => new CancellationReason { Code = i == failedIndex ? "ConditionalCheckFailed" : "None" }).ToList();

        Assert.Equal(expected, DynamoStockRepository.OutcomeOf(reasons, balances: 2, movements: 2));
    }

    [Fact]
    public void OutcomeOf_AnUnexpectedReason_IsNotSwallowed()
    {
        var reasons = new List<CancellationReason> { new() { Code = "ValidationError" }, new() { Code = "None" } };

        Assert.Throws<InvalidOperationException>(() => DynamoStockRepository.OutcomeOf(reasons, 1, 1));
    }

    [Fact]
    public async Task Save_ACancelledTransaction_ReturnsItsOutcome()
    {
        _dynamoDb.Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons = [new() { Code = "ConditionalCheckFailed" }, new() { Code = "None" }, new() { Code = "None" }],
            });
        var balance = StockBalance.Load(ProductId, Alpha, 3, 0);
        var movements = balance.Receive(1, "1", null, null, Context).Movements;

        var outcome = await _repository.SaveAsync(new StockWrite([balance], movements, [new WarehouseCondition("w-alpha", true)]), default);

        Assert.Equal(SaveOutcome.BalanceChanged, outcome);
    }

    [Fact]
    public async Task Load_AMissingRow_IsAnEmptyBalance_AndARow_KeepsItsValues()
    {
        _dynamoDb.SetupSequence(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = [] })
            .ReturnsAsync(new GetItemResponse { Item = new() { ["Available"] = new() { N = "9" }, ["Reserved"] = new() { N = "1" } } });

        var empty = await _repository.LoadAsync(ProductId, Alpha, default);
        var loaded = await _repository.LoadAsync(ProductId, Alpha, default);

        Assert.Null(empty.ReadAvailable);
        Assert.Equal(0, empty.Available);
        Assert.Equal(9, loaded.ReadAvailable);
        Assert.Equal(1, loaded.Reserved);
        _dynamoDb.Verify(d => d.GetItemAsync(It.Is<GetItemRequest>(r => r.ConsistentRead == true), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void ALedgerRow_RoundTrips()
    {
        var source = StockBalance.Load(ProductId, Alpha, 10, 0);
        var destination = StockBalance.Empty(ProductId, new WarehouseSnapshot("w-beta", "BETA", true));
        var rows = source.TransferTo(destination, 4, "note", Context).Movements;

        var back = rows.Select(row => DynamoStockRepository.ToMovement(DynamoStockRepository.ToItem(row))).ToList();

        Assert.Equal(rows, back);
    }

    private sealed class FixedTime(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at);
    }
}
