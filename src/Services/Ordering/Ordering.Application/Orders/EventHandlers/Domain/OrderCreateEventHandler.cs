using Microsoft.FeatureManagement;
using Ordering.Domain.AggregatesModel.OrderAggregate.Events;

namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreateEventHandler(
    IPublishEndpoint publishEndpoint,
    ILogger<OrderCreateEventHandler> logger,
    IFeatureManager featureManager)
    : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent domain, CancellationToken cancellationToken)
    {
        logger.LogInformation(domain.ToString());

        if (await featureManager.IsEnabledAsync("OrderFullfilment"))
        {
            await publishEndpoint.Publish(domain.order.ToOrderDto(), cancellationToken);
        }
    }
}
