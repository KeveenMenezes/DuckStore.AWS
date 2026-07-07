namespace Pricing.Function.Modules.Campaigns.Features.EndCampaign;

public record EndCampaignCommand(Guid CampaignId) : ICommand<EndCampaignResult>;

public record EndCampaignResult(Guid CampaignId, bool Ended);

public class EndCampaignCommandValidator : AbstractValidator<EndCampaignCommand>
{
    public EndCampaignCommandValidator()
    {
        RuleFor(x => x.CampaignId)
            .NotEmpty()
            .WithMessage("CampaignId is required");
    }
}
