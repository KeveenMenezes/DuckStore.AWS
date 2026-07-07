namespace Payment.Function.Modules.Payments.Domain.Enums;

// Intentionally duplicated from Ordering's own PaymentMethod enum — Payment must not take a
// cross-module dependency on Ordering.Function just to share this enum (module boundary, ADR-0019).
public enum PaymentMethod
{
    Debit = 1,
    Credit = 2
}
