namespace Pricing.Function.Shared.Exceptions;

public class PriceBadRequestException(decimal price)
    : BadRequestException(
        "NominalPrice",
        price);
