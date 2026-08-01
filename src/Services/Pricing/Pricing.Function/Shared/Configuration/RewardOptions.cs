namespace Pricing.Function.Shared.Configuration;

// v1 conversion is a fixed constant, not a tier catalogue (ADR-0046 §8) — Challenges sends a raw
// point quantity and never learns this rate (ADR-0046 §1). Read directly from the Rewards:*
// configuration keys, set via the Rewards__* env vars in PricingExtensions.cs.
public class RewardOptions
{
    public int PointsPerUnit { get; init; } = 100;
    public decimal CurrencyPerUnit { get; init; } = 10m;
    public int ExpiryDays { get; init; } = 90;

    // Not the enforcement point for a redemption minimum — Challenges owns and enforces that
    // (PlayerProgress.MinimumRedeemablePoints). This is only what rewardConversion surfaces so the
    // SPA can display the same figure before the customer redeems (ADR-0046 §1).
    public decimal ConvertToCurrency(int points) =>
        Math.Round(points * (CurrencyPerUnit / PointsPerUnit), 2, MidpointRounding.AwayFromZero);

    public static RewardOptions FromConfiguration(IConfiguration configuration)
    {
        var defaults = new RewardOptions();

        return new RewardOptions
        {
            // Parsing successfully is not the same as being usable: PointsPerUnit is a divisor, and
            // a zero or negative value from a typo'd env var would either throw inside every
            // redemption (DivideByZeroException on the DLQ, points debited but no reward minted) or
            // mint a negative discount that raises the cart total. Fall back to the default instead
            // — a wrong-but-sane rate is recoverable, a poisoned consumer is not.
            PointsPerUnit = int.TryParse(
                configuration["Rewards:PointsPerUnit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointsPerUnit)
                && pointsPerUnit > 0
                ? pointsPerUnit
                : defaults.PointsPerUnit,
            CurrencyPerUnit = decimal.TryParse(
                configuration["Rewards:CurrencyPerUnit"], NumberStyles.Number, CultureInfo.InvariantCulture, out var currencyPerUnit)
                && currencyPerUnit > 0
                ? currencyPerUnit
                : defaults.CurrencyPerUnit,
            ExpiryDays = int.TryParse(
                configuration["Rewards:ExpiryDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiryDays)
                && expiryDays > 0
                ? expiryDays
                : defaults.ExpiryDays
        };
    }
}
