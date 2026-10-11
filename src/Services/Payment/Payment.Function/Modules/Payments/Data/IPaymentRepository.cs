using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.Function.Modules.Payments.Data;

public interface IPaymentRepository
{
    Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // The order's payment, through GSI1 (ORDER#<orderId>) — eventually consistent, so a null may
    // only mean "not visible yet". A checkout creates exactly one payment per order (ADR-0038).
    Task<PaymentEntity?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task AddAsync(PaymentEntity payment, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
