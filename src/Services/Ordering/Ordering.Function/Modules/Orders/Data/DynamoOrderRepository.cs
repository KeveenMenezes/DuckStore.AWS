namespace Ordering.Function.Modules.Orders.Data;

// Each order is a single DynamoDB item keyed by Id, with its OrderItems embedded as a list
// attribute. A single PutItem is therefore atomic on its own — no TransactWriteItems needed.
// GSI1 lists orders by customer (GSI1PK=CUSTOMER#{id}, GSI1SK=CreatedAt) with ProjectionType.ALL,
// so reads don't need a follow-up GetItem per result. Publishing OrderCreatedEvent to EventBridge
// is done via DynamoDB Streams (the table has Streams enabled, consumed by the OrderCreated publisher).
public class DynamoOrderRepository(IAmazonDynamoDB dynamoDb) : IOrderRepository
{
    public const string TableName = "ordering";
    public const string Gsi1Name = "GSI1";

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) }
            },
            cancellationToken);

        return response.Item is { Count: > 0 } ? MapOrder(response.Item) : null;
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken = default) =>
        dynamoDb.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = ToItem(order) },
            cancellationToken);

    internal static TransactWriteItem ToTransactWriteItem(Order order) =>
        new() { Put = new Put { TableName = TableName, Item = ToItem(order) } };

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    internal static Dictionary<string, AttributeValue> ToItem(Order order)
    {
        var createdAt = (order.CreatedAt ?? DateTime.UtcNow).ToString("o");

        return new()
        {
            ["Id"] = new(order.Id.Value.ToString()),
            ["Type"] = new("Order"), // stream filter discriminator (see OrderCreated publisher)
            ["CustomerId"] = new(order.CustomerId.Value.ToString()),
            ["OrderName"] = new(order.OrderName.Value),
            ["Status"] = new(order.Status.ToString()),
            ["CreatedAt"] = new(createdAt),
            ["GSI1PK"] = new(CustomerGsiPk(order.CustomerId.Value)),
            ["GSI1SK"] = new(createdAt),
            ["ShippingAddress"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["FirstName"] = new(order.ShippingAddress.FirstName),
                    ["LastName"] = new(order.ShippingAddress.LastName),
                    ["EmailAddress"] = new(order.ShippingAddress.EmailAddress ?? string.Empty),
                    ["AddressLine"] = new(order.ShippingAddress.AddressLine),
                    ["Country"] = new(order.ShippingAddress.Country),
                    ["State"] = new(order.ShippingAddress.State),
                    ["ZipCode"] = new(order.ShippingAddress.ZipCode)
                }
            },
            ["Payment"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["CardName"] = new(order.Payment.CardName ?? string.Empty),
                    ["CardNumber"] = new(order.Payment.CardNumber),
                    ["Expiration"] = new(order.Payment.Expiration),
                    ["Cvv"] = new(order.Payment.Cvv),
                    ["PaymentMethod"] = new(order.Payment.PaymentMethod.ToString())
                }
            },
            ["OrderItems"] = new AttributeValue
            {
                L = [.. order.OrderItems.Select(item => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = new(item.Id.Value.ToString()),
                        ["ProductId"] = new(item.ProductId.Value.ToString()),
                        ["Quantity"] = new AttributeValue { N = item.Quantity.ToString(CultureInfo.InvariantCulture) },
                        ["Price"] = new AttributeValue { N = item.Price.ToString(CultureInfo.InvariantCulture) }
                    }
                })]
            }
        };
    }

    private static Order MapOrder(Dictionary<string, AttributeValue> item)
    {
        var id = Guid.Parse(item["Id"].S);

        var addressMap = item["ShippingAddress"].M;
        var paymentMap = item["Payment"].M;

        var address = Address.Of(
            addressMap["FirstName"].S,
            addressMap["LastName"].S,
            addressMap["EmailAddress"].S,
            addressMap["AddressLine"].S,
            addressMap["Country"].S,
            addressMap["State"].S,
            addressMap["ZipCode"].S);

        var payment = Payment.Of(
            paymentMap["CardName"].S,
            paymentMap["CardNumber"].S,
            paymentMap["Expiration"].S,
            paymentMap["Cvv"].S,
            Enum.Parse<PaymentMethod>(paymentMap["PaymentMethod"].S));

        var orderItems = (item.TryGetValue("OrderItems", out var items) ? items.L : [])
            .Select(i => OrderItem.Load(
                Guid.Parse(i.M["Id"].S),
                id,
                Guid.Parse(i.M["ProductId"].S),
                int.Parse(i.M["Quantity"].N, CultureInfo.InvariantCulture),
                decimal.Parse(i.M["Price"].N, CultureInfo.InvariantCulture)));

        return Order.Load(
            id,
            Guid.Parse(item["CustomerId"].S),
            item["OrderName"].S,
            address,
            payment,
            Enum.Parse<OrderStatus>(item["Status"].S),
            orderItems,
            DateTime.Parse(item["CreatedAt"].S, null, DateTimeStyles.RoundtripKind));
    }

    private static string CustomerGsiPk(Guid customerId) => $"CUSTOMER#{customerId}";
}
