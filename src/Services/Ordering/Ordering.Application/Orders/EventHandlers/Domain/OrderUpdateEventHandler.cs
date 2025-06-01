using Ordering.Domain.AggregatesModel.OrderAggregate.Events;

namespace Ordering.Application.Orders.EventHandlers.Domain;

public class OrderUpdateEventHandler(
    ILogger<OrderUpdateEventHandler> logger)
    : INotificationHandler<OrderUpdatedEvent>
{
    public Task Handle(OrderUpdatedEvent domain, CancellationToken cancellationToken)
    {
        logger.LogInformation(domain.ToString());

        return Task.CompletedTask;
    }
}
