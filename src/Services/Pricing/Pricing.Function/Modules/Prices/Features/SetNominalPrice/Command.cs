namespace Pricing.Function.Modules.Prices.Features.SetNominalPrice;

public record SetNominalPriceCommand(Guid ProductId, decimal NominalPrice, decimal Cost) : ICommand<SetNominalPriceResult>;

public record SetNominalPriceResult(Guid ProductId, decimal NominalPrice, decimal Cost);

public class SetNominalPriceCommandValidator : AbstractValidator<SetNominalPriceCommand>
{
    public SetNominalPriceCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage("ProductId is required");

        RuleFor(x => x.NominalPrice)
            .GreaterThan(0)
            .WithMessage("NominalPrice must be greater than zero");

        RuleFor(x => x.Cost)
            .GreaterThan(0)
            .WithMessage("Cost must be greater than zero");
    }
}
