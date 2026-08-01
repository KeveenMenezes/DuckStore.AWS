namespace Challenges.Function.Modules.Progress.Domain.Entities;

// One row per redemption — Id is a fresh identifier minted here, never client-supplied, so
// Put ... attribute_not_exists(SK) can never collide with a real prior redemption (ADR-0046 §2).
public class Redemption : Entity<string>
{
    public int Points { get; private set; }
    public DateTime RedeemedAt { get; private set; }

    private Redemption() { }

    internal static Redemption Create(int points, DateTime redeemedAt) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Points = points,
            RedeemedAt = redeemedAt
        };

    public static Redemption Load(string id, int points, DateTime redeemedAt) =>
        new()
        {
            Id = id,
            Points = points,
            RedeemedAt = redeemedAt
        };
}
