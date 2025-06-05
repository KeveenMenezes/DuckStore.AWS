namespace Catalog.API.Data;

public class IdentifiableAggregate<TKey, T> : Aggregate<TKey>
    where T : IComparable<T>
    where TKey : ValueObject<T>
{
    [JsonIgnore]
    public new IReadOnlyList<IDomainEvent> DomainEvents => base.DomainEvents;

    [Identity]
    public T AggregateId
    {
        get => Id.Value;
        set { }
    }
}
