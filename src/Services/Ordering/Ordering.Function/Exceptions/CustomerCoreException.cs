namespace Ordering.Function.Exceptions;

public class CustomerIdBadRequestException(Guid customerId)
    : BadRequestException(
        "CustomerId",
        customerId);
