using Basket.Function.Features.CheckoutBasket;

namespace Basket.Function;

public record CheckoutBasketRequest(BasketCheckoutDto BasketCheckoutDto);
public record CheckoutBasketResponse(bool IsSuccess);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Post, "/basket/checkout")]
    public async Task<IHttpResult> CheckoutBasket(
        [FromBody] CheckoutBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<CheckoutBasketCommand>();

        var result = await sender.Send(command, CancellationToken.None);

        var response = result.Adapt<CheckoutBasketResponse>();

        return HttpResults.Ok(response);
    }
}
