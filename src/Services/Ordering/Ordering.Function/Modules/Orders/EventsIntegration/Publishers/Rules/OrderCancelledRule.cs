namespace Ordering.Function.Modules.Orders.EventsIntegration.Publishers.Rules;

// Publishes OrderCancelledEvent when an order moves into Cancelled — from Pending (payment
// declined) or from Completed (cancelled after payment). Challenges consumes it to give back the
// points the checkout reserved or used (ADR-0048 §5). Matching the transition rather than the
// state means a redelivered stream record, or a later MODIFY of an already-cancelled order,
// publishes nothing — the points are never returned twice from here.
public sealed class OrderCancelledRule(IOrderRepository orders) : IStreamRule<OrderStreamImage>
{
    private static readonly string Cancelled = nameof(OrderStatus.Cancelled);

    public bool Match(StreamContext<OrderStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.New is { Type: "Order" } next
        && next.Status == Cancelled
        && context.Old?.Status != Cancelled;

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<OrderStreamImage> context, CancellationToken cancellationToken = default)
    {
        // The stream image carries only Id/Type/Status; the customer comes from the order itself.
        var order = await orders.GetByIdAsync(context.New!.Id, cancellationToken);

        return new PublishInstruction(nameof(OrderCancelledEvent), new OrderCancelledEvent
        {
            OrderId = order!.Id.Value,
            CustomerId = order.CustomerId.Value
        });
    }
}
