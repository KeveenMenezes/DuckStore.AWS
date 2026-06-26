using Basket.Function.Features.DeleteBasket;

namespace Basket.Function;

public record DeleteBasketResponse(bool IsSuccess);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Delete, "/basket/{userName}")]
    public async Task<DeleteBasketResponse> DeleteBasket(
        string userName,
        [FromServices] ISender sender)
    {
        var result = await sender.Send(new DeleteBasketCommand(userName), CancellationToken.None);

        return result.Adapt<DeleteBasketResponse>();
    }
}
