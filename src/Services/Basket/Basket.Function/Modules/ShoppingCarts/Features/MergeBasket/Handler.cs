namespace Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;

public class MergeBasketCommandHandler(IShoppingCartRepository repository)
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
