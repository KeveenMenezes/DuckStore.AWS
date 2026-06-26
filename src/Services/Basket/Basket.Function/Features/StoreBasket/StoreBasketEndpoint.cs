using Basket.Function.Features.StoreBasket;

namespace Basket.Function;

public record StoreBasketRequest(ShoppingCart Cart);
public record StoreBasketResponse(string UserName);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Post, "/basket")]
    public async Task<IHttpResult> StoreBasket(
        [FromBody] StoreBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<StoreBasketCommand>();

        var result = await sender.Send(command, CancellationToken.None);

        var response = result.Adapt<StoreBasketResponse>();

        return HttpResults.Created($"/basket/{response.UserName}", response);
    }
}
