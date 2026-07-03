namespace Basket.Function.Features.CheckoutBasket;

public record CheckoutBasketCommand(BasketCheckoutDto BasketCheckoutDto)
    : ICommand<CheckoutBasketResult>;

public record CheckoutBasketResult(bool IsSuccess);

public class CheckoutBasketCommandValidator
    : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        RuleFor(x => x.BasketCheckoutDto)
            .NotNull()
            .WithMessage("BasketCheckoutDto can't be null");

        RuleFor(x => x.BasketCheckoutDto.OwnerId)
            .NotEmpty()
            .WithMessage("OwnerId is required")
            .When(x => x.BasketCheckoutDto != null);
    }
}

public class CheckoutBasketCommandHandler(IBasketRepository basketRepository)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    public async Task<CheckoutBasketResult> Handle(
        CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.TryGetBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        if (basket == null)
        {
            return new CheckoutBasketResult(false);
        }

        // Write checkout payload to DynamoDB before deletion so DynamoDB Streams
        // captures it and the ShoppingCartsEventPublisher Lambda can publish to EventBridge.
        var checkoutDataJson = JsonSerializer.Serialize(command.BasketCheckoutDto);
        await basketRepository.MarkCheckoutAsync(
            command.BasketCheckoutDto.OwnerId, checkoutDataJson, cancellationToken);

        await basketRepository.DeleteBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        return new CheckoutBasketResult(true);
    }
}
