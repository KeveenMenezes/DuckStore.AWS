using Basket.Function.Features.GetBasket;

namespace Basket.Function;

public record GetBasketResponse(ShoppingCart Cart);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Get, "/basket/{userName}")]
    public async Task<IHttpResult> GetBasket(
        string userName,
        [FromServices] ISender sender)
    {
        try
        {
            var result = await sender.Send(new GetBasketQuery(userName), CancellationToken.None);

            var response = result.Adapt<GetBasketResponse>();

            return HttpResults.Ok(response);
        }
        catch (BasketNotFoundException)
        {
            return HttpResults.NotFound();
        }
    }
}
