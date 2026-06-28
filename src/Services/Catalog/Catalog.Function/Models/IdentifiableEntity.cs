namespace Catalog.Function.Models;

public class IdentifiableEntity<TKey, T> : Entity<TKey>
    where T : IComparable<T>
    where TKey : ValueObject<T>;
