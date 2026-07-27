using BuildingBlocks.Core.Validation;

namespace Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;

// Called right after login: moves the visitor's GUEST# cart into the authenticated USER# cart.
public record MergeBasketCommand(string OwnerId, string GuestId) : ICommand<MergeBasketResult>;
public record MergeBasketResult(string OwnerId);

public class MergeBasketCommandValidator : IValidator<MergeBasketCommand>
{
    public IEnumerable<ValidationFailure> Validate(MergeBasketCommand instance)
    {
        if (instance.OwnerId?.StartsWith("USER#", StringComparison.Ordinal) != true)
        {
            yield return new(nameof(instance.OwnerId), "OwnerId must be a USER# identity");
        }

        if (instance.GuestId?.StartsWith("GUEST#", StringComparison.Ordinal) != true)
        {
            yield return new(nameof(instance.GuestId), "GuestId must be a GUEST# identity");
        }
    }
}
