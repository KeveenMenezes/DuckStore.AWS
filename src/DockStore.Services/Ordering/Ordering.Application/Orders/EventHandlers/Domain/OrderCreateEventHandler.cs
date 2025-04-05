namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreateEventHandler(
    IPublishEndpoint publishEndpoint,
    ILogger<OrderCreateEventHandler> logger)
    : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent domain, CancellationToken cancellationToken)
    {
        logger.LogInformation("Domain Event handled: {DomaiEvent}", domain.GetType().Name);

        var orderCreatedIntegrationEvent = domain.order.ToOrderDto();

        await publishEndpoint.Publish(orderCreatedIntegrationEvent, cancellationToken);
    }
}
