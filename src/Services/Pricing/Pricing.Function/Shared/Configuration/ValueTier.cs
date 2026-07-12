namespace Pricing.Function.Shared.Configuration;

// A cart-total threshold that unlocks a floor on interest-free installments, independent of
// per-unit margin (ADR-0028 §2 hybrid model). InstallmentOptions.FromConfiguration guarantees
// ValueTiers is sorted ascending by MinAmount so callers can do a simple forward scan.
public sealed record ValueTier(decimal MinAmount, int MaxInstallments);
