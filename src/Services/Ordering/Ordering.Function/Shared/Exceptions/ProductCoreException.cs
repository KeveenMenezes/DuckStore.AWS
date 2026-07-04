namespace Ordering.Function.Shared.Exceptions;

public class ProductIdBadRequestException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId);
