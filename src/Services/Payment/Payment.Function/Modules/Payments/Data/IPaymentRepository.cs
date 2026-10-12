using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.Function.Modules.Payments.Data;

public interface IPaymentRepository
{
    // Strongly consistent: the checkout saga and the checkout consumer decide on the payment's
    // current status right after it was written.
    Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // The order's payment, through GSI1 (ORDER#<orderId>) — eventually consistent, so a null may
    // only mean "not visible yet". A checkout creates exactly one payment per order (ADR-0038).
    Task<PaymentEntity?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task AddAsync(PaymentEntity payment, CancellationToken cancellationToken = default);

    // Writes the payment only if none exists with its id; false when one already does.
    Task<bool> TryAddAsync(PaymentEntity payment, CancellationToken cancellationToken = default);

    // Overwrites the payment only while the stored one is still in expectedStatus; false otherwise,
    // so a concurrent transition wins over this write.
    Task<bool> ReplaceIfStatusAsync(
        PaymentEntity payment, PaymentStatus expectedStatus, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
