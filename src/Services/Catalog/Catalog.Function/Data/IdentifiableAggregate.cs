namespace Catalog.Function.Data;

public class IdentifiableAggregate<TKey, T> : Aggregate<TKey>
    where T : IComparable<T>
    where TKey : ValueObject<T>
{
}
