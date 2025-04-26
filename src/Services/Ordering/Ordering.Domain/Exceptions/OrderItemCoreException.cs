namespace Ordering.Domain.Exceptions;

public class OrderItemIdCoreException(Guid orderItemId)
    : DomainException(
        "OrderItemId",
        orderItemId);
