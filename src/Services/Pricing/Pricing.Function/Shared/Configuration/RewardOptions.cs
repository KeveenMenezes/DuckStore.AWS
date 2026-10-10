namespace Pricing.Function.Shared.Configuration;

// Points-to-currency conversion and the redemption cap — Pricing's alone: Challenges sends a raw
// point quantity and never learns the rate (ADR-0046 §1, ADR-0048 §3). Business rules: 100 points
// = R$1, at most 20% of the post-campaign cart price (SPEC.md). Read from the Rewards:* keys, set
// via the Rewards__* env vars (reward-config.ts on AWS, PricingExtensions.cs locally).
public class RewardOptions
{
    public int PointsPerUnit { get; init; } = 100;
    public decimal CurrencyPerUnit { get; init; } = 1m;
    public decimal MaxDiscountPercent { get; init; } = 20m;

    // Only the legacy coupon (CustomerDiscounts) expires; in-cart points never do. Goes away with
    // the coupon (T34).
    public int ExpiryDays { get; init; } = 90;

    public decimal ConvertToCurrency(int points) =>
        Math.Round(points * (CurrencyPerUnit / PointsPerUnit), 2, MidpointRounding.AwayFromZero);

    // Whole points, rounded down: the cap may never be exceeded by a fraction of a point.
    public int MaxRedeemablePoints(decimal total) =>
        total <= 0
            ? 0
            : (int)Math.Floor(total * MaxDiscountPercent / 100m * PointsPerUnit / CurrencyPerUnit);

    // Rounded down to whole cents: the store never gives away more than the points are worth.
    public decimal PointsDiscount(int points) =>
        points <= 0
            ? 0m
            : Math.Floor(points * CurrencyPerUnit / PointsPerUnit * 100m) / 100m;

    public static RewardOptions FromConfiguration(IConfiguration configuration)
    {
        var defaults = new RewardOptions();

        return new RewardOptions
        {
            // Parsing successfully is not the same as being usable: PointsPerUnit is a divisor, and
            // a zero or negative value from a typo'd env var would either throw inside every quote
            // or mint a negative discount that raises the cart total. Fall back to the default
            // instead — a wrong-but-sane rate is recoverable, a poisoned Lambda is not.
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
            // Above 100% points could pay more than the cart itself; that's a typo, not a policy.
            MaxDiscountPercent = decimal.TryParse(
                configuration["Rewards:MaxDiscountPercent"], NumberStyles.Number, CultureInfo.InvariantCulture, out var maxDiscountPercent)
                && maxDiscountPercent is > 0 and <= 100
                ? maxDiscountPercent
                : defaults.MaxDiscountPercent,
            ExpiryDays = int.TryParse(
                configuration["Rewards:ExpiryDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiryDays)
                && expiryDays > 0
                ? expiryDays
                : defaults.ExpiryDays
        };
    }
}
