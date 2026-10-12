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

    // DeclineReason for a checkout whose points couldn't be reserved (ADR-0048 §6).
    public const string InsufficientPointsReason = "InsufficientPoints";

    // awaitingPoints: the checkout used points, so the payment waits for their reservation before
    // the gateway is asked — otherwise it could be authorized with a discount the customer didn't
    // have (ADR-0048 §6). Without points the payment starts Pending, as it always has.
    public static Payment Create(
        PaymentId id, Guid orderId, Guid customerId, decimal amount, CardDetails card, bool awaitingPoints = false)
    {
        return new Payment
        {
            Id = id,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Card = card,
            Status = awaitingPoints ? PaymentStatus.AwaitingPoints : PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    // Written by the checkout saga before the cart closes, keyed PaymentId = OrderId so a retry of
    // the same checkout finds it (ADR-0050 §6). Registered never reaches the gateway; the checkout
    // event promotes it, or the saga voids it when the cart changed.
    public static Payment Register(PaymentId id, Guid orderId, Guid customerId, decimal amount, CardDetails card)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        ArgumentNullException.ThrowIfNull(card);

        return new Payment
        {
            Id = id,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Card = card,
            Status = PaymentStatus.Registered,
            CreatedAt = DateTime.UtcNow
        };
    }

    // A repeated registration is a replay only while nothing happened to the payment since and the
    // terms are the same; installments are part of the amount, the method part of the card.
    public bool IsSameRegistration(decimal amount, CardDetails card) =>
        Status == PaymentStatus.Registered && Amount == amount && Card == card;

    // The cart closed: the payment moves on as a checkout-created one always did — waiting for the
    // points reservation when the checkout used points. Returns false (no-op) unless Registered, so
    // a redelivered event or a payment the saga already voided is left alone.
    public bool Promote(bool awaitingPoints)
    {
        if (Status != PaymentStatus.Registered)
            return false;

        Status = awaitingPoints ? PaymentStatus.AwaitingPoints : PaymentStatus.Pending;
        return true;
    }

    // The saga's compensation when the cart changed after registration. Returns false (no-op)
    // unless Registered: a promoted payment belongs to a closed cart and must not be undone.
    public bool Void()
    {
        if (Status != PaymentStatus.Registered)
            return false;

        Status = PaymentStatus.Voided;
        return true;
    }

    // The points were reserved: the payment goes on to the gateway (PaymentRequestedRule matches
    // this transition). A no-op once the payment left AwaitingPoints, so a redelivery is harmless.
    public void ConfirmPoints()
    {
        EnsurePromoted();
        if (Status != PaymentStatus.AwaitingPoints)
            return;

        Status = PaymentStatus.Pending;
    }

    // The balance was short: declined without asking the gateway. PaymentDeclinedInsufficientPointsRule
    // announces it so Ordering cancels the order.
    public void RejectPoints()
    {
        EnsurePromoted();
        if (Status != PaymentStatus.AwaitingPoints)
            return;

        Status = PaymentStatus.Declined;
        DeclineReason = InsufficientPointsReason;
    }

    // Challenges reserves the points off the same checkout event that promotes a Registered payment,
    // so its outcome can arrive first. Unlike any other status, Registered will still move to
    // AwaitingPoints: the outcome must be retried, not dropped as a no-op (ADR-0050 §6).
    private void EnsurePromoted()
    {
        if (Status == PaymentStatus.Registered)
            throw new PaymentNotYetPromotedException(OrderId);
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
