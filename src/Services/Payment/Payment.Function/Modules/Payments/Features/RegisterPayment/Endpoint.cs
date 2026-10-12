using Payment.Function.Modules.Payments.Features.RegisterPayment;

namespace Payment.Function;

// Invoked synchronously by the checkout-saga Express state machine only — no Function URL, no
// event source (ADR-0050 §2). Validates the card and writes the order's payment as Registered,
// idempotently per OrderId; InvalidCard / CheckoutRetryRequired come back as data, never as a
// thrown error, so the saga can tell a refusal from a failure (ADR-0050 §6/§10).
public partial class Functions
{
    [LambdaFunction]
    public Task<RegisterPaymentResponse> RegisterPayment(
        RegisterPaymentRequest request,
        [FromServices] RegisterPaymentHandler handler) =>
        handler.HandleAsync(request);
}
