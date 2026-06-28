namespace Ordering.Function.Features.GetOrdersByCustomer;

public record GetOrdersByCustomerQuery(Guid CustomerId)
    : IQuery<GetOrdersByCustomerResult>;

public record GetOrdersByCustomerResult(IReadOnlyList<OrderDto> Orders);
