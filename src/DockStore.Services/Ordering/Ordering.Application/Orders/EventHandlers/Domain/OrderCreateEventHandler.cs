namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreateEventHandler(
    ILogger<OrderCreateEventHandler> logger)
    : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Domain Event handled: {DomaiEvent}", notification.GetType().Name);
        return Task.CompletedTask;
    }
}
