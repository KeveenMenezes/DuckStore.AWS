namespace Pricing.Function.Shared.Exceptions;

public class CampaignPeriodBadRequestException(DateTime startsAt, DateTime endsAt)
    : BadRequestException(
        "EndsAt",
        endsAt,
        $"must be after StartsAt ({startsAt:O})");
