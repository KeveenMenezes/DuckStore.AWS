namespace Ordering.Function;

public record DeleteOrderRequest(Guid OrderId);

public record DeleteOrderResponse(bool IsDeleted);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<DeleteOrderResponse> DeleteOrder(
        DeleteOrderRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<DeleteOrderCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<DeleteOrderResponse>();
    }
}
