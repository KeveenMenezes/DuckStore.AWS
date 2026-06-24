using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;
using Ordering.Domain.AggregatesModel.OrderAggregate.Models;
using Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;
using Ordering.Domain.Enums;

namespace Ordering.Infrastructure.RepositoryAdapters;

// Modelo single-table: Order (PK=ORDER#{id}, SK=ORDER) + OrderItems (PK=ORDER#{id}, SK=ORDERITEM#{itemId}).
// Order+OrderItems são escritos atomicamente via TransactWriteItems. A publicação do
// OrderCreatedEvent para o EventBridge não depende mais disso — é feita via DynamoDB Streams
// (a tabela tem Streams habilitado, consumido por Ordering.OrderCreatedPublisher.Lambda).
public class OrderRepository(IAmazonDynamoDB dynamoDb)
    : IOrderRepository
{
    public const string TableName = "OrderingTable";
    public const string Gsi1Name = "GSI1";
    public const string Gsi2Name = "GSI2";

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "PK = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = new(OrderPk(id)) }
            },
            cancellationToken);

        return response.Items.Count == 0 ? null : MapOrder(response.Items);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default) =>
        await dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest { TransactItems = BuildOrderTransactItems(order) },
            cancellationToken);

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default) =>
        await dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest { TransactItems = BuildOrderTransactItems(order) },
            cancellationToken);

    public async Task DeleteAsync(Order order, CancellationToken cancellationToken = default)
    {
        var transactItems = new List<TransactWriteItem>
        {
            new() { Delete = new Delete { TableName = TableName, Key = OrderKey(order.Id.Value) } }
        };

        transactItems.AddRange(order.OrderItems.Select(item =>
            new TransactWriteItem
            {
                Delete = new Delete { TableName = TableName, Key = OrderItemKey(order.Id.Value, item.Id.Value) }
            }));

        await dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest { TransactItems = transactItems },
            cancellationToken);
    }

    public async IAsyncEnumerable<Order> GetOrdersByCustomerAsync(Guid customerId)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = Gsi1Name,
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = new(CustomerGsiPk(customerId)) },
            ScanIndexForward = false // GSI1SK = CreatedAt: pedidos mais recentes primeiro
        });

        foreach (var item in response.Items)
        {
            var order = await GetByIdAsync(Guid.Parse(item["Id"].S));
            if (order is not null)
                yield return order;
        }
    }

    // GSI2 já projeta os atributos do cabeçalho do pedido (sem itens), evitando um GetItem extra por resultado.
    public async IAsyncEnumerable<Order> GetOrdersByStatusAsync(OrderStatus status)
    {
        var response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = Gsi2Name,
            KeyConditionExpression = "GSI2PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = new(StatusGsiPk(status)) },
            ScanIndexForward = false // GSI2SK = CreatedAt: pedidos mais recentes primeiro
        });

        foreach (var item in response.Items)
            yield return MapOrderHeader(item);
    }

    public async IAsyncEnumerable<Order> GetOrdersByNameAsync(string name)
    {
        foreach (var item in await ScanOrderRowsAsync("contains(OrderName, :name)", (":name", new AttributeValue(name))))
        {
            var order = await GetByIdAsync(Guid.Parse(item["Id"].S));
            if (order is not null)
                yield return order;
        }
    }

    public async IAsyncEnumerable<Order> GetOrdersPaginationStream(int pageIndex, int pageSize)
    {
        var rows = await ScanOrderRowsAsync();
        var page = rows
            .OrderBy(o => o["OrderName"].S, StringComparer.Ordinal)
            .Skip(pageIndex * pageSize)
            .Take(pageSize);

        foreach (var item in page)
        {
            var order = await GetByIdAsync(Guid.Parse(item["Id"].S));
            if (order is not null)
                yield return order;
        }
    }

    public async Task<long> GetTotalCountOrders(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest
            {
                TableName = TableName,
                FilterExpression = "#type = :type",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "Type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":type"] = new("Order") },
                Select = Select.COUNT
            },
            cancellationToken);

        return response.Count ?? 0;
    }

    private async Task<List<Dictionary<string, AttributeValue>>> ScanOrderRowsAsync(
        string? extraFilter = null, (string Name, AttributeValue Value)? extraValue = null)
    {
        var filterExpression = "#type = :type";
        var values = new Dictionary<string, AttributeValue> { [":type"] = new("Order") };

        if (extraFilter is not null)
        {
            filterExpression += $" AND {extraFilter}";
            values[extraValue!.Value.Name] = extraValue.Value.Value;
        }

        var response = await dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = TableName,
            FilterExpression = filterExpression,
            ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "Type" },
            ExpressionAttributeValues = values
        });

        return response.Items;
    }

    private static List<TransactWriteItem> BuildOrderTransactItems(Order order)
    {
        var transactItems = new List<TransactWriteItem>
        {
            new() { Put = new Put { TableName = TableName, Item = ToOrderRow(order) } }
        };

        transactItems.AddRange(order.OrderItems.Select(item =>
            new TransactWriteItem
            {
                Put = new Put { TableName = TableName, Item = ToOrderItemRow(order.Id.Value, item) }
            }));

        return transactItems;
    }

    private static Dictionary<string, AttributeValue> ToOrderRow(Order order)
    {
        var createdAt = (order.CreatedAt ?? DateTime.UtcNow).ToString("o");
        var updatedAt = (order.LastModified ?? order.CreatedAt ?? DateTime.UtcNow).ToString("o");

        return new()
        {
            ["PK"] = new(OrderPk(order.Id.Value)),
            ["SK"] = new(OrderSk),
            ["Type"] = new("Order"),
            ["Id"] = new(order.Id.Value.ToString()),
            ["CustomerId"] = new(order.CustomerId.Value.ToString()),
            ["OrderName"] = new(order.OrderName.Value),
            ["Status"] = new(order.Status.ToString()),
            ["CreatedAt"] = new(createdAt),
            ["UpdatedAt"] = new(updatedAt),
            ["GSI1PK"] = new(CustomerGsiPk(order.CustomerId.Value)),
            ["GSI1SK"] = new(createdAt),
            ["GSI2PK"] = new(StatusGsiPk(order.Status)),
            ["GSI2SK"] = new(createdAt),
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
            }
        };
    }

    private static Dictionary<string, AttributeValue> ToOrderItemRow(Guid orderId, OrderItem item) =>
        new()
        {
            ["PK"] = new(OrderPk(orderId)),
            ["SK"] = new(OrderItemSk(item.Id.Value)),
            ["Type"] = new("OrderItem"),
            ["Id"] = new(item.Id.Value.ToString()),
            ["ProductId"] = new(item.ProductId.Value.ToString()),
            ["Quantity"] = new AttributeValue { N = item.Quantity.ToString(CultureInfo.InvariantCulture) },
            ["Price"] = new AttributeValue { N = item.Price.ToString(CultureInfo.InvariantCulture) }
        };

    private static Order MapOrder(List<Dictionary<string, AttributeValue>> items)
    {
        var orderRow = items.First(i => i["Type"].S == "Order");
        var itemRows = items.Where(i => i["Type"].S == "OrderItem");

        var id = Guid.Parse(orderRow["Id"].S);

        var orderItems = itemRows.Select(i => OrderItem.Load(
            Guid.Parse(i["Id"].S),
            id,
            Guid.Parse(i["ProductId"].S),
            int.Parse(i["Quantity"].N, CultureInfo.InvariantCulture),
            decimal.Parse(i["Price"].N, CultureInfo.InvariantCulture)));

        return BuildOrder(orderRow, orderItems);
    }

    // GSI2 usa ProjectionType.ALL, então o item já traz tudo que BuildOrder precisa (sem itens da
    // linha) — usado por GetOrdersByStatusAsync para listar pedidos por status sem GetItem extra.
    private static Order MapOrderHeader(Dictionary<string, AttributeValue> orderRow) => BuildOrder(orderRow, []);

    private static Order BuildOrder(Dictionary<string, AttributeValue> orderRow, IEnumerable<OrderItem> orderItems)
    {
        var addressMap = orderRow["ShippingAddress"].M;
        var paymentMap = orderRow["Payment"].M;

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

        return Order.Load(
            Guid.Parse(orderRow["Id"].S),
            Guid.Parse(orderRow["CustomerId"].S),
            orderRow["OrderName"].S,
            address,
            payment,
            Enum.Parse<OrderStatus>(orderRow["Status"].S),
            orderItems,
            DateTime.Parse(orderRow["CreatedAt"].S, null, DateTimeStyles.RoundtripKind),
            DateTime.Parse(orderRow["UpdatedAt"].S, null, DateTimeStyles.RoundtripKind));
    }

    private static string OrderPk(Guid orderId) => $"ORDER#{orderId}";
    private static string OrderItemSk(Guid orderItemId) => $"ORDERITEM#{orderItemId}";
    private static string CustomerGsiPk(Guid customerId) => $"CUSTOMER#{customerId}";
    private static string StatusGsiPk(OrderStatus status) => $"STATUS#{status.ToString().ToUpperInvariant()}";
    private const string OrderSk = "ORDER";

    private static Dictionary<string, AttributeValue> OrderKey(Guid orderId) =>
        new() { ["PK"] = new(OrderPk(orderId)), ["SK"] = new(OrderSk) };

    private static Dictionary<string, AttributeValue> OrderItemKey(Guid orderId, Guid orderItemId) =>
        new() { ["PK"] = new(OrderPk(orderId)), ["SK"] = new(OrderItemSk(orderItemId)) };
}
