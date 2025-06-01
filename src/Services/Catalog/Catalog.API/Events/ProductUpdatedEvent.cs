namespace Catalog.API.Events;

public record ProductUpdatedEvent(Product product) : IDomainEvent;
