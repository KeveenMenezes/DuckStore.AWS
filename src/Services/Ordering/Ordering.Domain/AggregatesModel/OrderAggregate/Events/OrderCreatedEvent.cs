namespace Ordering.Domain.AggregatesModel.OrderAggregate.Events;

public record OrderCreatedEvent(Order order) : IDomainEvent;
