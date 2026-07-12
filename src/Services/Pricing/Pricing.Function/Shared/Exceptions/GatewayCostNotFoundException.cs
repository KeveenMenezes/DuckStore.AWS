namespace Pricing.Function.Shared.Exceptions;

public class GatewayCostNotFoundException(string provider)
    : BadRequestException(
        "Provider",
        provider,
        "no gateway cost configuration exists for the active provider");
