namespace Ordering.Function.Exceptions;

public class OrderItemIdCoreException(Guid orderItemId)
    : DomainException(
        "OrderItemId",
        orderItemId);
