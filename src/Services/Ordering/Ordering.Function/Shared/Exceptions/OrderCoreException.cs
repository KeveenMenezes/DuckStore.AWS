namespace Ordering.Function.Shared.Exceptions;

public class OrderIdBadRequestException(Guid orderId)
    : BadRequestException(
        "OrderId",
        orderId);
