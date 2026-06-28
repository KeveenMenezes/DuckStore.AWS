namespace BuildingBlocks.Core.DomainModel;

// Aggregate-root marker. Kept to preserve the DDD aggregate boundary in the type
// system. Integration events are published via DynamoDB Streams (CDC), not via
// in-process domain events — see ADR-0005.
public abstract class Aggregate<TId> : Entity<TId>, IAggregate<TId>
{
}
