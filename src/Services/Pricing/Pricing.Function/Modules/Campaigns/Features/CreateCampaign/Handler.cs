namespace Pricing.Function.Modules.Campaigns.Features.CreateCampaign;

public class CreateCampaignHandler(ICampaignRepository campaignRepository)
    : ICommandHandler<CreateCampaignCommand, CreateCampaignResult>
{
    public async Task<CreateCampaignResult> Handle(
        CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            command.Name,
            DiscountValue.Of(command.DiscountType, command.Value),
            command.StartsAt,
            command.EndsAt,
            command.ProductIds.Select(ProductId.Of));

        await campaignRepository.AddAsync(campaign, cancellationToken);

        return new CreateCampaignResult(campaign.Id.Value);
    }
}
