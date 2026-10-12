using Payment.Function.Modules.Payments.Features.VoidPayment;

namespace Payment.Function;

// Invoked synchronously by the checkout-saga Express state machine only, as its compensation when
// Checkout refused with CartChanged (ADR-0050 §1/§2). Moves the order's payment Registered → Voided;
// Voided = false when there was nothing to void, so retries are safe.
public partial class Functions
{
    [LambdaFunction]
    public Task<VoidPaymentResponse> VoidPayment(
        VoidPaymentRequest request,
        [FromServices] VoidPaymentHandler handler) =>
        handler.HandleAsync(request);
}
