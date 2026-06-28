namespace BuildingBlocks.Core.DomainModel;

// Aggregate-root markers — see ADR-0005.
public interface IAggregate<T> : IAggregate, IEntity<T>
{
}

public interface IAggregate : IEntity
{
}
