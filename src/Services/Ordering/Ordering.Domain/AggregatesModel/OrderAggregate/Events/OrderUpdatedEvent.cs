namespace Ordering.Domain.AggregatesModel.OrderAggregate.Events;

public record OrderUpdatedEvent(Order order) : IDomainEvent;
