namespace Payment.Function.Modules.Payments.Features.VoidPayment;

public class VoidPaymentHandler(IPaymentRepository paymentRepository)
{
    public async Task<VoidPaymentResponse> HandleAsync(
        VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await paymentRepository.GetByIdAsync(PaymentId.Of(request.OrderId).Value, cancellationToken);

        if (payment is null || !payment.Void())
            return new VoidPaymentResponse(Voided: false);

        // Conditioned on Registered: if the checkout event promoted the payment after the read, the
        // promotion wins and nothing is voided.
        var voided = await paymentRepository.ReplaceIfStatusAsync(
            payment, PaymentStatus.Registered, cancellationToken);

        return new VoidPaymentResponse(voided);
    }
}
