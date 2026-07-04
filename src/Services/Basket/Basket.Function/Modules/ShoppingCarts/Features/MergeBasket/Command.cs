namespace Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;

// Called right after login: moves the visitor's GUEST# cart into the authenticated USER# cart.
public record MergeBasketCommand(string OwnerId, string GuestId) : ICommand<MergeBasketResult>;
public record MergeBasketResult(string OwnerId);

public class MergeBasketCommandValidator : AbstractValidator<MergeBasketCommand>
{
    public MergeBasketCommandValidator()
    {
        RuleFor(x => x.OwnerId)
            .Must(id => id?.StartsWith("USER#", StringComparison.Ordinal) == true)
            .WithMessage("OwnerId must be a USER# identity");

        RuleFor(x => x.GuestId)
            .Must(id => id?.StartsWith("GUEST#", StringComparison.Ordinal) == true)
            .WithMessage("GuestId must be a GUEST# identity");
    }
}
