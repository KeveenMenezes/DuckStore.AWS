namespace Ordering.Domain.Exceptions;

public class OrderNotFoundBadRequestException(Guid orderId)
    : NotFoundException(
        "OrderId",
        orderId);

public class StatusBadRequestException(string status)
    : BadRequestException(
        "Status"
        , status);

public class OrderIdBadRequestException(Guid orderId)
    : BadRequestException(
        "OrderId",
        orderId);
