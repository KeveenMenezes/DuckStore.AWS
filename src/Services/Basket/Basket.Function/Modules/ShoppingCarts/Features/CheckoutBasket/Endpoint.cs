using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

namespace Basket.Function;

public record CheckoutBasketRequest(BasketCheckoutDto BasketCheckoutDto);
public record CheckoutBasketResponse(bool IsSuccess);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<CheckoutBasketResponse> CheckoutBasket(
        CheckoutBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<CheckoutBasketCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<CheckoutBasketResponse>();
    }
}
