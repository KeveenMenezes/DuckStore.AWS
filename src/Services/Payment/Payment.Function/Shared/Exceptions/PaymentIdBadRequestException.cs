namespace Payment.Function.Shared.Exceptions;

public class PaymentIdBadRequestException(Guid paymentId)
    : BadRequestException(
        "PaymentId",
        paymentId);
