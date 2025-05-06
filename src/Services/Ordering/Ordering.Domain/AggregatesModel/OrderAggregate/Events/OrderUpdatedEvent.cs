using BuildingBlocks.Core.DomainModel;

namespace Ordering.Domain.AggregatesModel.OrderAggregate.Events;

public record OrderUpdatedEvent(Order order) : IDomainEvent;
