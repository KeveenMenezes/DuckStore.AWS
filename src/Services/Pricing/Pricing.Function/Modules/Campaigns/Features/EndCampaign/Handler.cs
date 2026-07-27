namespace Pricing.Function.Modules.Campaigns.Features.EndCampaign;

public class EndCampaignHandler(ICampaignRepository campaignRepository)
    : ICommandHandler<EndCampaignCommand, EndCampaignResult>
{
    public async ValueTask<EndCampaignResult> Handle(
        EndCampaignCommand command, CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.GetByIdAsync(command.CampaignId, cancellationToken)
            ?? throw new CampaignIdBadRequestException(command.CampaignId);

        campaign.Cancel();

        await campaignRepository.CancelAsync(campaign, cancellationToken);

        return new EndCampaignResult(campaign.Id.Value, true);
    }
}
