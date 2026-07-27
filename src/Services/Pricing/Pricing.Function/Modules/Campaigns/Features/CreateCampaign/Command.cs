using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Campaigns.Features.CreateCampaign;

public record CreateCampaignCommand(
    string Name,
    DiscountType DiscountType,
    decimal Value,
    DateTime StartsAt,
    DateTime EndsAt,
    List<Guid> ProductIds) : ICommand<CreateCampaignResult>;

public record CreateCampaignResult(Guid Id);

public class CreateCampaignCommandValidator : IValidator<CreateCampaignCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateCampaignCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Name))
        {
            yield return new(nameof(instance.Name), "Name is required");
        }

        if (!Enum.IsDefined(instance.DiscountType))
        {
            yield return new(nameof(instance.DiscountType), "Invalid discount type");
        }

        if (instance.Value <= 0)
        {
            yield return new(nameof(instance.Value), "Value must be greater than zero");
        }

        if (instance.EndsAt <= instance.StartsAt)
        {
            yield return new(nameof(instance.EndsAt), "EndsAt must be after StartsAt");
        }

        if (instance.ProductIds is null || instance.ProductIds.Count == 0)
        {
            yield return new(nameof(instance.ProductIds), "ProductIds should not be empty");
        }
    }
}
