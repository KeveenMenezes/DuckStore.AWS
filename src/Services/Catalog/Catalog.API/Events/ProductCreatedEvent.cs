namespace Catalog.API.Events;

public record ProductCreatedEvent(Product product) : IDomainEvent;
