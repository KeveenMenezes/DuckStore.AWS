namespace Challenges.Function.Modules.Progress.Domain.ValueObjects;

// "USER#<cognito-sub>" only — unlike Basket's OwnerId there is no GUEST# form here: guests play
// but never score (ADR-0045 §7), so every write path requires a real Cognito identity.
public class OwnerId : ValueObject<string>
{
    private OwnerId(string value) : base(value) { }

    public static OwnerId Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("USER#", StringComparison.Ordinal))
        {
            throw new OwnerIdBadRequestException(value);
        }

        return new OwnerId(value);
    }
}
