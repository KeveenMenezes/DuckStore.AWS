using Ordering.Domain.AggregatesModel.OrderAggregate.Events;

namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderCreateEventHandler(ILogger<OrderCreateEventHandler> logger)
    : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent domain, CancellationToken cancellationToken)
    {
        logger.LogInformation(domain.ToString());

        return Task.CompletedTask;
    }
}