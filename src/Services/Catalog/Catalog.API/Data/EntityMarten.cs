namespace Catalog.API.Data;

public class IdentifiableEntity<TKey, T> : Entity<TKey>
    where T : IComparable<T>
    where TKey : ValueObject<T>
{
    [Identity]
    public T AggregateId
    {
        get => Id.Value;
        set { }
    }
}
