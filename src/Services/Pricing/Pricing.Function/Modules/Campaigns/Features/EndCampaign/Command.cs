using BuildingBlocks.Core.Validation;

namespace Pricing.Function.Modules.Campaigns.Features.EndCampaign;

public record EndCampaignCommand(Guid CampaignId) : ICommand<EndCampaignResult>;

public record EndCampaignResult(Guid CampaignId, bool Ended);

public class EndCampaignCommandValidator : IValidator<EndCampaignCommand>
{
    public IEnumerable<ValidationFailure> Validate(EndCampaignCommand instance)
    {
        if (instance.CampaignId == Guid.Empty)
        {
            yield return new(nameof(instance.CampaignId), "CampaignId is required");
        }
    }
}
