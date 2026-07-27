using Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;

namespace Basket.Function;

public record MergeBasketRequest(string OwnerId, string GuestId);
public record MergeBasketResponse(string OwnerId);

// Triggered by direct AppSync Invoke (no Function URL) from the mergeBasket resolver. Folds a
// GUEST# cart into the USER# cart on login (ADR-0016): reads both carts, writes the merged
// USER# cart, deletes the GUEST# cart.
public partial class Functions
{
    [LambdaFunction]
    public async Task<MergeBasketResponse> MergeBasket(
        MergeBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = new MergeBasketCommand(request.OwnerId, request.GuestId);
        var result = await sender.Send(command, CancellationToken.None);
        return new MergeBasketResponse(result.OwnerId);
    }
}
