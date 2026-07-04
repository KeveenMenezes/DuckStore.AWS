namespace Basket.Function.Features.MergeBasket;

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

public class MergeBasketCommandHandler(IBasketRepository repository)
    : ICommandHandler<MergeBasketCommand, MergeBasketResult>
{
    public async Task<MergeBasketResult> Handle(MergeBasketCommand command, CancellationToken cancellationToken)
    {
        var guestCart = await repository.TryGetBasket(command.GuestId, cancellationToken);

        // Nothing to merge — idempotent no-op (also covers a duplicate/late merge call).
        if (guestCart is null || guestCart.Items.Count == 0)
            return new MergeBasketResult(command.OwnerId);

        var userCart = await repository.TryGetBasket(command.OwnerId, cancellationToken)
            ?? ShoppingCart.Create(command.OwnerId, []);

        userCart.Merge(guestCart);

        await repository.MergeAsync(userCart, command.GuestId, cancellationToken);

        return new MergeBasketResult(command.OwnerId);
    }
}
