namespace Basket.API.Basket.StoreBasket;

public record StoreBasketCommand(ShoppingCart Cart)
    : ICommand<StoreBasketResult>;
public record StoreBasketResult(string UserName);

public class StoreBasketCommandValidator
    : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart)
            .NotNull()
            .WithMessage("Cart cannot be null");

        RuleFor(x => x.Cart.UserName)
            .NotNull()
            .NotEmpty()
            .WithMessage("UserName cannot be empty or null");
    }
}

public class StoreBasketCommandHandler : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var cart = command.Cart;

        //TODO: store basket in database (use marten upsert or insert)
        //TODO: update the cache

        return new StoreBasketResult("swd");
    }
}



