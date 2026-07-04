using Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;

namespace Basket.Function;

public record MergeBasketRequest(string OwnerId, string GuestId);
public record MergeBasketResponse(string OwnerId);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<MergeBasketResponse> MergeBasket(
        MergeBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<MergeBasketCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<MergeBasketResponse>();
    }
}
