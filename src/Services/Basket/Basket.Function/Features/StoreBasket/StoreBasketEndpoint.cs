using Basket.Function.Features.StoreBasket;

namespace Basket.Function;

public record StoreBasketRequest(ShoppingCartDto Cart);
public record StoreBasketResponse(string UserName);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<StoreBasketResponse> StoreBasket(
        StoreBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<StoreBasketCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<StoreBasketResponse>();
    }
}
