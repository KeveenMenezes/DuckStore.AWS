namespace Payment.Function.Modules.Payments.Domain.Entities;

public class Payment : Aggregate<PaymentId>
{
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public CardDetails Card { get; private set; } = null!;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? AuthorizationCode { get; private set; }
    public string? DeclineReason { get; private set; }

    public static Payment Create(PaymentId id, Guid orderId, Guid customerId, decimal amount, CardDetails card)
    {
        return new Payment
        {
            Id = id,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Card = card,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    // Maps a Payment gateway's authorize/decline outcome onto the payment's own transition, so
    // callers never branch on Authorized themselves.
    public void ApplyPaymentResult(bool authorized, string detail)
    {
        if (authorized)
            Authorize(detail);
        else
            Decline(detail);
    }

    // Guards against re-applying a duplicate result delivery beyond what the idempotency inbox
    // already prevents — a Payment only ever leaves Pending once.
    private void Authorize(string authorizationCode)
    {
        if (Status != PaymentStatus.Pending)
            return;

        Status = PaymentStatus.Authorized;
        AuthorizationCode = authorizationCode;
    }

    private void Decline(string reason)
    {
        if (Status != PaymentStatus.Pending)
            return;

        Status = PaymentStatus.Declined;
        DeclineReason = reason;
    }

    public static Payment Load(
        Guid id,
        Guid orderId,
        Guid customerId,
        decimal amount,
        CardDetails card,
        PaymentStatus status,
        string? authorizationCode,
        string? declineReason,
        DateTime? createdAt = null,
        DateTime? lastModified = null)
    {
        return new Payment
        {
            Id = PaymentId.Of(id),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Card = card,
            Status = status,
            AuthorizationCode = authorizationCode,
            DeclineReason = declineReason,
            CreatedAt = createdAt,
            LastModified = lastModified
        };
    }
}
