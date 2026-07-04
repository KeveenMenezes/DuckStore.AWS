namespace User.Function.Modules.Users.Models;

// Extended customer profile owned by the User bounded context (ADR-0017). Cognito remains the
// IdP (email/name/password); everything here lives in the `user-profiles` table.
public class UserProfile : Aggregate<string>
{
    private UserProfile(string userId) => Id = userId;

    // UserId is the profile's identity (the `user-profiles` partition key) = Cognito sub.
    public string UserId => Id;

    // Email and Name are Cognito-authoritative; seeded from the token and not edited here.
    public string Email { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    public string? Phone { get; private set; }
    public string? AddressLine { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? ZipCode { get; private set; }
    public string? Country { get; private set; }

    // Lazy provisioning: created on first authenticated access, seeded from Cognito claims.
    public static UserProfile Create(string userId, string email, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new UserProfile(userId)
        {
            Email = email,
            Name = string.IsNullOrWhiteSpace(name) ? email : name
        };
    }

    // Reconstitutes a persisted profile.
    public static UserProfile Load(
        string userId, string email, string name,
        string? phone, string? addressLine, string? city,
        string? state, string? zipCode, string? country) =>
        new(userId)
        {
            Email = email,
            Name = name,
            Phone = phone,
            AddressLine = addressLine,
            City = city,
            State = state,
            ZipCode = zipCode,
            Country = country
        };
}
