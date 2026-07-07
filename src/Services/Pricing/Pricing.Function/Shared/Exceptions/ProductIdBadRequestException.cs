namespace Pricing.Function.Shared.Exceptions;

public class ProductIdBadRequestException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId);
