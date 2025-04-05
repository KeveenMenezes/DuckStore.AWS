namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreateEventHandler(
    IPublishEndpoint publishEndpoint,
    ILogger<OrderCreateEventHandler> logger,
    IFeatureManager featureManager)
    : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent domain, CancellationToken cancellationToken)
    {
        logger.LogInformation("Domain Event handled: {DomaiEvent}", domain.GetType().Name);

        if (await featureManager.IsEnabledAsync("OrderCreatedDomainEvent"))
        {
            await publishEndpoint.Publish(domain.order.ToOrderDto(), cancellationToken);
        }
    }
}
