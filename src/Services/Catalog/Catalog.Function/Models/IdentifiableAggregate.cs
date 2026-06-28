namespace Catalog.Function.Models;

public class IdentifiableAggregate<TKey, T> : Aggregate<TKey>
    where T : IComparable<T>
    where TKey : ValueObject<T>
{
}
