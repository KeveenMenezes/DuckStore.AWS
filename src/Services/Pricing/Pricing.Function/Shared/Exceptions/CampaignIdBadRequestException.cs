namespace Pricing.Function.Shared.Exceptions;

public class CampaignIdBadRequestException(Guid campaignId)
    : BadRequestException(
        "CampaignId",
        campaignId);
