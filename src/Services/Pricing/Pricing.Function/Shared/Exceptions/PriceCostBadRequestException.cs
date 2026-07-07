namespace Pricing.Function.Shared.Exceptions;

public class PriceCostBadRequestException(decimal cost)
    : BadRequestException(
        "Cost",
        cost);
