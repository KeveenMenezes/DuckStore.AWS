namespace Ordering.Domain.Exceptions;

public class CustomerIdBadRequestException(Guid customerId)
    : BadRequestException(
        "CustomerId",
        customerId);
