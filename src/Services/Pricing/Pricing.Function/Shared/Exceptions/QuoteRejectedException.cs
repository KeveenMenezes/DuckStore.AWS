namespace Pricing.Function.Shared.Exceptions;

// A cart quote Pricing refuses to honour, with a stable ErrorType the caller can branch on without
// parsing the message (ADR-0048 §3). The GetBasketInstallmentPlan endpoint returns it as a typed
// rejection instead of failing the invocation, so the AppSync resolver can raise it as a GraphQL
// error carrying errorType and errorInfo.
public abstract class QuoteRejectedException(string errorType, string message) : Exception(message)
{
    public string ErrorType { get; } = errorType;
}

// More points than the 20% cap allows. Rejected, never trimmed: silently using fewer points than
// the customer asked for would charge a total they didn't see (SPEC-cart-points-redemption §1).
public sealed class PointsAboveCapException(int pointsToUse, int maxRedeemablePoints)
    : QuoteRejectedException(
        "PointsAboveCap",
        $"Cannot use {pointsToUse} points on this cart: at most {maxRedeemablePoints} can be redeemed.")
{
    public int MaxRedeemablePoints { get; } = maxRedeemablePoints;
}

// The total the customer confirmed is not the one Pricing computes now — a price or campaign
// changed, or the client was tampered with. The checkout is refused rather than charging a total
// the customer didn't see (SPEC-cart-points-redemption §2).
public sealed class PriceChangedException(decimal serverTotal)
    : QuoteRejectedException(
        "PriceChanged",
        "The cart total changed. Review the new total before checking out.")
{
    public decimal ServerTotal { get; } = serverTotal;
}
