namespace Ordering.Function;

public record GetOrdersByCustomerRequest(Guid CustomerId);

public record GetOrdersByCustomerResponse(List<OrderDto> Orders);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<GetOrdersByCustomerResponse> GetOrdersByCustomer(
        GetOrdersByCustomerRequest request,
        [FromServices] ISender sender)
    {
        var result = await sender.Send(
            new GetOrdersByCustomerQuery(request.CustomerId),
            CancellationToken.None);

        return new GetOrdersByCustomerResponse([.. result.Orders]);
    }
}
