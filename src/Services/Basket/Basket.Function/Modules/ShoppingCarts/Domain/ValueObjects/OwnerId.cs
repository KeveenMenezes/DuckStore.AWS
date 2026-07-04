namespace Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects;

// Wraps the cart's identity: "USER#<cognito-sub>" (authenticated) or "GUEST#<guestId>" (visitor).
public class OwnerId : ValueObject<string>
{
    private OwnerId(string value) : base(value) { }

    public bool IsGuest => Value.StartsWith("GUEST#", StringComparison.Ordinal);

    public static OwnerId Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !(value.StartsWith("USER#", StringComparison.Ordinal) || value.StartsWith("GUEST#", StringComparison.Ordinal)))
        {
            throw new OwnerIdBadRequestException(value);
        }

        return new OwnerId(value);
    }
}
