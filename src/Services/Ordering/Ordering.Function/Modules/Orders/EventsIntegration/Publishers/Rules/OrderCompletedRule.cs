namespace Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

// Publishes OrderCompletedEvent when an order moves into Completed (ADR-0049 §3). Matching the
// transition rather than the state means a redelivered stream record, or a later MODIFY of an
// already-completed order, publishes nothing. Declined/cancelled orders never reach Completed.
public sealed class OrderCompletedRule(IOrderRepository orders) : IStreamRule<OrderStreamImage>
{
    private static readonly string Completed = nameof(OrderStatus.Completed);

    public bool Match(StreamContext<OrderStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.New is { Type: "Order" } next
        && next.Status == Completed
        && context.Old?.Status != Completed;

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<OrderStreamImage> context, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByIdAsync(context.New!.Id, cancellationToken);

        return new PublishInstruction(nameof(OrderCompletedEvent), ToOrderCompletedEvent(order!));
    }

    private static OrderCompletedEvent ToOrderCompletedEvent(Order order) => new()
    {
        OrderId = order.Id.Value,
        CustomerId = order.CustomerId.Value,
        ProductIds = [.. order.OrderItems.Select(oi => oi.ProductId.Value).Distinct()]
    };
}
