using Inventory.Function.Modules.Stock.Data;
using Inventory.Function.Modules.Stock.Domain;
using Inventory.Function.Modules.Stock.Domain.Enums;
using Inventory.Function.Modules.Stock.Features;
using Inventory.Function.Modules.Stock.Features.RecordAdjustment;
using Inventory.Function.Modules.Stock.Features.RecordReceipt;
using Inventory.Function.Modules.Stock.Features.TransferStock;
using Inventory.Function.Modules.Warehouses.Data;
using Inventory.Function.Modules.Warehouses.Domain;

namespace Inventory.UnitTests.Modules.Stock.Features;

public class MovementHandlersTests
{
    private const string Key = "11111111-2222-4333-8444-555555555555";
    private const string ProductId = "p-1";
    private static readonly WarehouseSnapshot Alpha = new("w-alpha", "ALPHA", true);
    private static readonly WarehouseSnapshot Beta = new("w-beta", "BETA", true);
    private readonly Mock<IStockRepository> _stock = new();
    private readonly Mock<IWarehouseReader> _warehouses = new();
    private readonly List<StockWrite> _writes = [];

    public MovementHandlersTests()
    {
        _warehouses.Setup(w => w.GetAsync("w-alpha", It.IsAny<CancellationToken>())).ReturnsAsync(Alpha);
        _warehouses.Setup(w => w.GetAsync("w-beta", It.IsAny<CancellationToken>())).ReturnsAsync(Beta);
        _stock.Setup(s => s.FindMovementsAsync(ProductId, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _stock.Setup(s => s.LoadAsync(ProductId, It.IsAny<WarehouseSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string product, WarehouseSnapshot warehouse, CancellationToken _) => StockBalance.Load(product, warehouse, 10, 0));
        SaveReturns(SaveOutcome.Saved);
    }

    private MovementRecorder Recorder => new(_stock.Object, TimeProvider.System);

    private RecordReceiptHandler ReceiptHandler => new(Recorder, _warehouses.Object, _stock.Object);

    private static RecordReceiptCommand Receipt(string key = Key, int quantity = 5, string invoice = "NF-1") =>
        new(key, ProductId, "w-alpha", quantity, invoice, null, null, "sub-1");

    private void SaveReturns(params SaveOutcome[] outcomes)
    {
        var queue = new Queue<SaveOutcome>(outcomes);
        _stock.Setup(s => s.SaveAsync(It.IsAny<StockWrite>(), It.IsAny<CancellationToken>()))
            .Callback<StockWrite, CancellationToken>((write, _) => _writes.Add(write))
            .ReturnsAsync(() => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
    }

    [Fact]
    public async Task Receipt_IsRecorded_WithTheCallersSub_AndAnActiveWarehouseCheck()
    {
        var result = await ReceiptHandler.Handle(Receipt(), default);

        var row = Assert.Single(result.Movements);
        Assert.Null(result.Refusal);
        Assert.Equal("sub-1", row.CreatedBy);
        Assert.Equal(15, row.BalanceAfter);
        Assert.Equal(new WarehouseCondition("w-alpha", true), Assert.Single(Assert.Single(_writes).Warehouses));
    }

    [Fact]
    public async Task AKeyThatIsNotAUuid_IsABadRequest_BeforeAnyRead()
    {
        var result = await ReceiptHandler.Handle(Receipt(key: "not-a-uuid"), default);

        Assert.Equal("BadRequest", result.Refusal!.ErrorType);
        Assert.Equal("idempotencyKey", result.Refusal.Field);
        _stock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AReplayOfTheSameRequest_ReturnsTheRecordedRow_WithoutDecidingAgain()
    {
        var command = Receipt();
        var recorded = Row(command.Fingerprint());
        _stock.Setup(s => s.FindMovementsAsync(ProductId, It.Is<IReadOnlyList<string>>(ids => ids.Single() == Key), It.IsAny<CancellationToken>()))
            .ReturnsAsync([recorded]);

        var result = await ReceiptHandler.Handle(command, default);

        Assert.Same(recorded, Assert.Single(result.Movements));
        _stock.Verify(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<WarehouseSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_writes);
    }

    [Fact]
    public async Task TheSameKeyWithADifferentRequest_IsIdempotencyKeyReused()
    {
        _stock.Setup(s => s.FindMovementsAsync(ProductId, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(Receipt(quantity: 99).Fingerprint())]);

        var result = await ReceiptHandler.Handle(Receipt(quantity: 5), default);

        Assert.Equal("IdempotencyKeyReused", result.Refusal!.ErrorType);
        Assert.Empty(_writes);
    }

    [Fact]
    public async Task AConcurrentChange_IsRetried_UntilTheWriteGoesThrough()
    {
        SaveReturns(SaveOutcome.BalanceChanged, SaveOutcome.WarehouseChanged, SaveOutcome.Saved);

        var result = await ReceiptHandler.Handle(Receipt(), default);

        Assert.Null(result.Refusal);
        Assert.Equal(3, _writes.Count);
        _stock.Verify(s => s.LoadAsync(ProductId, It.IsAny<WarehouseSnapshot>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RetriesRunOut_AsARetryableTransactionConflict()
    {
        SaveReturns(SaveOutcome.BalanceChanged);

        var result = await ReceiptHandler.Handle(Receipt(), default);

        Assert.Equal("TransactionConflict", result.Refusal!.ErrorType);
        Assert.Equal(MovementRecorder.MaxAttempts, _writes.Count);
    }

    [Fact]
    public async Task LosingTheRaceToATwinRequest_AnswersAsTheReplay()
    {
        var command = Receipt();
        var twin = Row(command.Fingerprint());
        _stock.SetupSequence(s => s.FindMovementsAsync(ProductId, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([twin]);
        SaveReturns(SaveOutcome.MovementExists);

        var result = await ReceiptHandler.Handle(command, default);

        Assert.Same(twin, Assert.Single(result.Movements));
    }

    [Fact]
    public async Task AnUnknownWarehouse_IsWarehouseNotFound()
    {
        var result = await ReceiptHandler.Handle(Receipt() with { WarehouseId = "nope" }, default);

        Assert.Equal("WarehouseNotFound", result.Refusal!.ErrorType);
        Assert.Equal("warehouseId", result.Refusal.Field);
    }

    [Fact]
    public async Task ADomainRefusal_WritesNothing()
    {
        var result = await ReceiptHandler.Handle(Receipt(invoice: " "), default);

        Assert.Equal("invoiceNumber", result.Refusal!.Field);
        Assert.Empty(_writes);
    }

    [Theory]
    [InlineData(StockAdjustmentDirection.Increase, true)]
    [InlineData(StockAdjustmentDirection.Decrease, false)]
    public async Task Adjustment_ChecksTheWarehouseIsActive_OnlyForAnIncrease(StockAdjustmentDirection direction, bool mustBeActive)
    {
        var handler = new RecordAdjustmentHandler(Recorder, _warehouses.Object, _stock.Object);

        await handler.Handle(new RecordAdjustmentCommand(Key, ProductId, "w-alpha", direction, 1, StockAdjustmentReason.Count, null, "sub-1"), default);

        Assert.Equal(new WarehouseCondition("w-alpha", mustBeActive), Assert.Single(Assert.Single(_writes).Warehouses));
    }

    [Fact]
    public async Task Transfer_WritesBothBalancesAndBothRows_AndOnlyTheDestinationMustBeActive()
    {
        var handler = new TransferStockHandler(Recorder, _warehouses.Object, _stock.Object);

        var result = await handler.Handle(new TransferStockCommand(Key, ProductId, "w-beta", "w-alpha", 4, null, "sub-1"), default);

        Assert.Equal(2, result.Movements.Count);
        var write = Assert.Single(_writes);
        Assert.Equal(["w-beta", "w-alpha"], write.Balances.Select(b => b.Warehouse.Id));
        Assert.Equal([new WarehouseCondition("w-beta", false), new WarehouseCondition("w-alpha", true)], write.Warehouses);
        _stock.Verify(s => s.FindMovementsAsync(ProductId,
            It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { $"{Key}#OUT", $"{Key}#IN" })), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void TheFingerprint_IgnoresSurroundingSpaces_ButNotTheContent()
    {
        Assert.Equal(Receipt(invoice: "NF-1").Fingerprint(), Receipt(invoice: " NF-1 ").Fingerprint());
        Assert.NotEqual(Receipt(invoice: "NF-1").Fingerprint(), Receipt(invoice: "NF-2").Fingerprint());
        Assert.NotEqual(Receipt(quantity: 5).Fingerprint(), Receipt(quantity: 6).Fingerprint());
    }

    private static StockMovement Row(string hash) => new(
        ProductId, Key, "w-alpha", "ALPHA", StockMovementType.Receipt, 5, 15, "NF-1", null, null, null, null, null,
        hash, DateTime.UtcNow, "sub-1");
}
