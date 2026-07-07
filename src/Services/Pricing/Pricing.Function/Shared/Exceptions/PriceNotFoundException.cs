namespace Pricing.Function.Shared.Exceptions;

public class PriceNotFoundException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId,
        "no nominal price has been set for this product");
