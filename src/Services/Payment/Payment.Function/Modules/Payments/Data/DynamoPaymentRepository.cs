using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.Function.Modules.Payments.Data;

// Each Payment is a single DynamoDB item keyed by Id — a single PutItem (or, when applying a
// result, a single overwriting Put inside the idempotency transaction) is atomic on its own,
// mirroring Ordering's DynamoOrderRepository. GSI1 lists payments by order
// (GSI1PK=ORDER#{orderId}, GSI1SK=CreatedAt) with ProjectionType.ALL. Publishing
// PaymentRequestedEvent to EventBridge is done via DynamoDB Streams (the table has Streams
// enabled, consumed by the PaymentRequested publisher).
public class DynamoPaymentRepository(IAmazonDynamoDB dynamoDb) : IPaymentRepository
{
    public const string TableName = "payments";
    public const string Gsi1Name = "GSI1";

    public async Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapPayment(response.Item) : null;
    }

    public Task AddAsync(PaymentEntity payment, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(payment) },
            cancellationToken);

    internal static TransactWriteItem ToTransactWriteItem(PaymentEntity payment) =>
        new() { Put = new Put { TableName = TableName, Item = ToItem(payment) } };

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    internal static Dictionary<string, AttributeValue> ToItem(PaymentEntity payment)
    {
        var createdAt = (payment.CreatedAt ?? DateTime.UtcNow).ToString("o");

        var item = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(payment.Id.Value.ToString()),
            ["Type"] = new("Payment"), // stream filter discriminator (see PaymentRequested publisher)
            ["OrderId"] = new(payment.OrderId.ToString()),
            ["CustomerId"] = new(payment.CustomerId.ToString()),
            ["Amount"] = new AttributeValue { N = payment.Amount.ToString(CultureInfo.InvariantCulture) },
            ["Status"] = new(payment.Status.ToString()),
            ["CreatedAt"] = new(createdAt),
            ["GSI1PK"] = new(OrderGsiPk(payment.OrderId)),
            ["GSI1SK"] = new(createdAt),
            ["Card"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["CardNumber"] = new(payment.Card.CardNumber),
                    ["Expiration"] = new(payment.Card.Expiration),
                    ["Cvv"] = new(payment.Card.Cvv),
                    ["PaymentMethod"] = new(payment.Card.PaymentMethod.ToString())
                }
            }
        };

        if (payment.AuthorizationCode is not null)
            item["AuthorizationCode"] = new AttributeValue(payment.AuthorizationCode);

        if (payment.DeclineReason is not null)
            item["DeclineReason"] = new AttributeValue(payment.DeclineReason);

        return item;
    }

    private static PaymentEntity MapPayment(Dictionary<string, AttributeValue> item)
    {
        var cardMap = item["Card"].M;

        var card = CardDetails.Of(
            cardMap["CardNumber"].S,
            cardMap["Expiration"].S,
            cardMap["Cvv"].S,
            Enum.Parse<PaymentMethod>(cardMap["PaymentMethod"].S));

        return PaymentEntity.Load(
            Guid.Parse(item["Id"].S),
            Guid.Parse(item["OrderId"].S),
            Guid.Parse(item["CustomerId"].S),
            decimal.Parse(item["Amount"].N, CultureInfo.InvariantCulture),
            card,
            Enum.Parse<PaymentStatus>(item["Status"].S),
            item.TryGetValue("AuthorizationCode", out var authCode) ? authCode.S : null,
            item.TryGetValue("DeclineReason", out var declineReason) ? declineReason.S : null,
            DateTime.Parse(item["CreatedAt"].S, null, DateTimeStyles.RoundtripKind));
    }

    private static string OrderGsiPk(Guid orderId) => $"ORDER#{orderId}";
}
