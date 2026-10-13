namespace Inventory.Function.Modules.Stock.Data;

public sealed class DynamoStockRepository(IAmazonDynamoDB dynamoDb, TimeProvider timeProvider) : IStockRepository
{
    // Sortable as a string, which is what LSI1/GSI1 order the history by.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public async Task<StockBalance> LoadAsync(string productId, WarehouseSnapshot warehouse, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = StockSchema.TableName,
            Key = BalanceKey(productId, warehouse.Id),
            ConsistentRead = true,
        }, cancellationToken);

        var item = response.Item;
        if (item is null || item.Count == 0)
            return StockBalance.Empty(productId, warehouse);

        return StockBalance.Load(
            productId, warehouse,
            NumberOf(item, StockSchema.AvailableAttribute),
            NumberOf(item, StockSchema.ReservedAttribute));
    }

    public async Task<IReadOnlyList<StockMovement>> FindMovementsAsync(
        string productId, IReadOnlyList<string> movementIds, CancellationToken cancellationToken)
    {
        // One consistent GetItem per row (one or two): unlike BatchGetItem, nothing comes back
        // partially processed, and the rows return in the order asked for (OUT before IN).
        var found = new List<StockMovement>();
        foreach (var movementId in movementIds)
        {
            var response = await dynamoDb.GetItemAsync(new GetItemRequest
            {
                TableName = MovementsSchema.TableName,
                Key = MovementKey(productId, movementId),
                ConsistentRead = true,
            }, cancellationToken);
            if (response.Item is { Count: > 0 } item)
                found.Add(ToMovement(item));
        }

        return found;
    }

    // One TransactWriteItems (ADR-0052 §7): balances first, then ledger rows, then warehouse checks —
    // the order the cancellation reasons are read back in.
    public async Task<SaveOutcome> SaveAsync(StockWrite write, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var items = new List<TransactWriteItem>();
        items.AddRange(write.Balances.Select(balance => BalanceUpdate(balance, now)));
        items.AddRange(write.Movements.Select(LedgerPut));
        items.AddRange(write.Warehouses.Select(WarehouseCheck));

        try
        {
            await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = items }, cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (TransactionCanceledException ex)
        {
            return OutcomeOf(ex.CancellationReasons, write.Balances.Count, write.Movements.Count);
        }
    }

    // A taken ledger key wins over everything else: the request is a replay and must be answered as one.
    internal static SaveOutcome OutcomeOf(IReadOnlyList<CancellationReason> reasons, int balances, int movements)
    {
        bool Failed(int from, int count) =>
            reasons.Skip(from).Take(count).Any(reason => reason.Code == "ConditionalCheckFailed");

        if (Failed(balances, movements)) return SaveOutcome.MovementExists;
        if (Failed(balances + movements, reasons.Count)) return SaveOutcome.WarehouseChanged;
        if (Failed(0, balances) || reasons.Any(reason => reason.Code == "TransactionConflict")) return SaveOutcome.BalanceChanged;

        throw new InvalidOperationException(
            $"Stock transaction cancelled for an unexpected reason: {string.Join(", ", reasons.Select(r => r.Code))}");
    }

    private static TransactWriteItem BalanceUpdate(StockBalance balance, string now) => new()
    {
        Update = new Update
        {
            TableName = StockSchema.TableName,
            Key = BalanceKey(balance.ProductId, balance.Warehouse.Id),
            UpdateExpression =
                "SET Available = :available, Reserved = if_not_exists(Reserved, :zero), WarehouseCode = :code, UpdatedAt = :now " +
                // Every balance write bumps the counter the storefront total is ordered by (ADR-0052 §8).
                "ADD Version :one",
            // Written against the value read: a concurrent movement makes this one retry (ADR-0052 §7).
            // Reserved too: a reservation settling concurrently (ADR-0052 §6) changes what is on hand.
            ConditionExpression = balance.ReadAvailable is null
                ? "attribute_not_exists(ProductId)"
                : "Available = :read AND Reserved = :reserved",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":available"] = Number(balance.Available),
                [":zero"] = Number(0),
                [":one"] = Number(1),
                [":code"] = new AttributeValue { S = balance.Warehouse.Code },
                [":now"] = new AttributeValue { S = now },
            }
            .With(balance.ReadAvailable is { } read ? (":read", Number(read)) : null)
            .With(balance.ReadAvailable is not null ? (":reserved", Number(balance.Reserved)) : null),
        },
    };

    private static TransactWriteItem LedgerPut(StockMovement movement) => new()
    {
        Put = new Put
        {
            TableName = MovementsSchema.TableName,
            Item = ToItem(movement),
            ConditionExpression = "attribute_not_exists(MovementId)",
        },
    };

    private static TransactWriteItem WarehouseCheck(WarehouseCondition condition) => new()
    {
        ConditionCheck = new ConditionCheck
        {
            TableName = WarehousesSchema.TableName,
            Key = new() { [WarehousesSchema.PartitionKey] = new AttributeValue { S = condition.WarehouseId } },
            ConditionExpression = condition.MustBeActive ? "#type = :warehouse AND Active = :true" : "#type = :warehouse",
            ExpressionAttributeNames = new() { ["#type"] = WarehousesSchema.TypeAttribute },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":warehouse"] = new AttributeValue { S = WarehousesSchema.WarehouseType },
            }.With(condition.MustBeActive ? (":true", new AttributeValue { BOOL = true }) : null),
        },
    };

    internal static Dictionary<string, AttributeValue> ToItem(StockMovement movement)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [MovementsSchema.PartitionKey] = new() { S = movement.ProductId },
            [MovementsSchema.SortKey] = new() { S = movement.MovementId },
            [MovementsSchema.WarehouseIdAttribute] = new() { S = movement.WarehouseId },
            ["WarehouseCode"] = new() { S = movement.WarehouseCode },
            ["Type"] = new() { S = movement.Type.ToString() },
            ["Quantity"] = Number(movement.Quantity),
            ["BalanceAfter"] = Number(movement.BalanceAfter),
            ["PayloadHash"] = new() { S = movement.PayloadHash },
            [MovementsSchema.CreatedAtAttribute] = new() { S = movement.CreatedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture) },
            ["CreatedBy"] = new() { S = movement.CreatedBy },
        };
        Optional(item, "InvoiceNumber", movement.InvoiceNumber);
        Optional(item, "Supplier", movement.Supplier);
        Optional(item, "Reason", movement.Reason?.ToString());
        Optional(item, "Note", movement.Note);
        Optional(item, "TransferId", movement.TransferId);
        Optional(item, "CounterpartWarehouseId", movement.CounterpartWarehouseId);
        return item;
    }

    internal static StockMovement ToMovement(Dictionary<string, AttributeValue> item) => new(
        item[MovementsSchema.PartitionKey].S,
        item[MovementsSchema.SortKey].S,
        item[MovementsSchema.WarehouseIdAttribute].S,
        item["WarehouseCode"].S,
        Enum.Parse<StockMovementType>(item["Type"].S),
        NumberOf(item, "Quantity"),
        NumberOf(item, "BalanceAfter"),
        StringOf(item, "InvoiceNumber"),
        StringOf(item, "Supplier"),
        StringOf(item, "Reason") is { } reason ? Enum.Parse<StockAdjustmentReason>(reason) : null,
        StringOf(item, "Note"),
        StringOf(item, "TransferId"),
        StringOf(item, "CounterpartWarehouseId"),
        item["PayloadHash"].S,
        DateTime.Parse(item[MovementsSchema.CreatedAtAttribute].S, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
        item["CreatedBy"].S);

    private static Dictionary<string, AttributeValue> BalanceKey(string productId, string warehouseId) => new()
    {
        [StockSchema.PartitionKey] = new AttributeValue { S = productId },
        [StockSchema.SortKey] = new AttributeValue { S = warehouseId },
    };

    private static Dictionary<string, AttributeValue> MovementKey(string productId, string movementId) => new()
    {
        [MovementsSchema.PartitionKey] = new AttributeValue { S = productId },
        [MovementsSchema.SortKey] = new AttributeValue { S = movementId },
    };

    private static AttributeValue Number(int value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static int NumberOf(Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var value) && value.N is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 0;

    private static string? StringOf(Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var value) ? value.S : null;

    private static void Optional(Dictionary<string, AttributeValue> item, string name, string? value)
    {
        if (value is not null) item[name] = new AttributeValue { S = value };
    }
}

internal static class AttributeValuesExtensions
{
    public static Dictionary<string, AttributeValue> With(
        this Dictionary<string, AttributeValue> values, (string Key, AttributeValue Value)? extra)
    {
        if (extra is { } pair) values[pair.Key] = pair.Value;
        return values;
    }
}
