namespace Payment.Function.Modules.Payments.Features.RegisterPayment;

// Not a Mediator command on purpose: the request carries the card, and nothing on this path may
// log it (ADR-0050 §7).
public class RegisterPaymentHandler(IPaymentRepository paymentRepository, TimeProvider clock)
{
    public async Task<RegisterPaymentResponse> HandleAsync(
        RegisterPaymentRequest request, CancellationToken cancellationToken = default)
    {
        CardDetails card;
        try
        {
            card = CardDetails.Validated(
                request.CardNumber ?? string.Empty,
                request.Expiration ?? string.Empty,
                request.Cvv ?? string.Empty,
                (PaymentMethod)request.PaymentMethod,
                clock);
        }
        catch (InvalidCardException ex)
        {
            return Refused(PaymentRefusals.InvalidCard, ex.Message, ex.Field);
        }

        var payment = Domain.Entities.Payment.Register(
            PaymentId.Of(request.OrderId), request.OrderId, request.CustomerId, request.Amount, card);

        if (await paymentRepository.TryAddAsync(payment, cancellationToken))
            return new RegisterPaymentResponse(payment.Id.Value);

        // The order already has a payment: the saga retrying the same checkout gets it back; anything
        // else (voided, promoted, other terms) can't be honored with this checkoutId (ADR-0050 §6).
        var existing = await paymentRepository.GetByIdAsync(payment.Id.Value, cancellationToken);

        return existing is not null && existing.IsSameRegistration(request.Amount, card)
            ? new RegisterPaymentResponse(existing.Id.Value)
            : Refused(
                PaymentRefusals.CheckoutRetryRequired,
                "This checkout can no longer be completed; start a new one.");
    }

    private static RegisterPaymentResponse Refused(string errorType, string message, string? field = null) =>
        new(null, new PaymentRefusalResponse(errorType, message, field));
}
