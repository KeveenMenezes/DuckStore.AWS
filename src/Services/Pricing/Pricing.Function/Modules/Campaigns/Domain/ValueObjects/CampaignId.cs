namespace Pricing.Function.Modules.Campaigns.Domain.ValueObjects;

public class CampaignId : ValueObject<Guid>
{
    private CampaignId(Guid value) : base(value) { }

    public static CampaignId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new CampaignIdBadRequestException(value);
        }

        return new CampaignId(value);
    }
}
