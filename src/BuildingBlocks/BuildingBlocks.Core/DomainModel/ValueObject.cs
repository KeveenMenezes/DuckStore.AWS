namespace BuildingBlocks.Core.DomainModel;

// Base for multi-field value objects: equality is by the ordered sequence of GetEqualityComponents().
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;

        var other = (ValueObject)obj;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, (hash, component) => HashCode.Combine(hash, component));

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}

public class ValueObject<T>
    : IEquatable<ValueObject<T>>
    where T : IComparable<T>
{
    public T Value { get; }

    public ValueObject(T value)
    {
        Value = value;
    }

    public bool Equals(ValueObject<T>? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<T>.Default.Equals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((ValueObject<T>)obj);
    }

    public override int GetHashCode()
    {
        return EqualityComparer<T>.Default.GetHashCode(Value);
    }

    public static bool operator ==(ValueObject<T>? left, ValueObject<T>? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ValueObject<T>? left, ValueObject<T>? right)
    {
        return !Equals(left, right);
    }
}
