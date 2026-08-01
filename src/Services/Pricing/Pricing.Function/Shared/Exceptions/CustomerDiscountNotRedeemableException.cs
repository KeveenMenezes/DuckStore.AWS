namespace Pricing.Function.Shared.Exceptions;

// Covers "doesn't exist", "belongs to a different owner", "already consumed" and "expired" alike
// (ADR-0046 §5) — CustomerDiscount.IsRedeemableBy already collapses all four into one boolean, and
// this stays undifferentiated on purpose: a caller probing discount ids should not be able to
// distinguish "not yours" from "doesn't exist" from the error alone.
public class CustomerDiscountNotRedeemableException(string discountId)
    : BadRequestException(nameof(CustomerDiscount), discountId, "is not redeemable by this customer");
