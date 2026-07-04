namespace Ordering.Function.Shared.Exceptions;

public class CustomerIdBadRequestException(Guid customerId)
    : BadRequestException(
        "CustomerId",
        customerId);
