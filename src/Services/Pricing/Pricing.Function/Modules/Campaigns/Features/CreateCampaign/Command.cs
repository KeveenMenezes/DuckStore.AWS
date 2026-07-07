namespace Pricing.Function.Modules.Campaigns.Features.CreateCampaign;

public record CreateCampaignCommand(
    string Name,
    DiscountType DiscountType,
    decimal Value,
    DateTime StartsAt,
    DateTime EndsAt,
    List<Guid> ProductIds) : ICommand<CreateCampaignResult>;

public record CreateCampaignResult(Guid Id);

public class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required");

        RuleFor(x => x.DiscountType)
            .IsInEnum()
            .WithMessage("Invalid discount type");

        RuleFor(x => x.Value)
            .GreaterThan(0)
            .WithMessage("Value must be greater than zero");

        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt)
            .WithMessage("EndsAt must be after StartsAt");

        RuleFor(x => x.ProductIds)
            .NotEmpty()
            .WithMessage("ProductIds should not be empty");
    }
}
