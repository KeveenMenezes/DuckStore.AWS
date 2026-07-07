namespace Pricing.Function.Shared.Exceptions;

public class GatewayCostBadRequestException(string reason)
    : BadRequestException(
        "GatewayCost",
        reason);
