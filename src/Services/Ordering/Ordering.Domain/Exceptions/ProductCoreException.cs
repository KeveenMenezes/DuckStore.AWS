namespace Ordering.Domain.Exceptions;

public class ProductNotFoundBadRequestException(Guid productId)
    : NotFoundException(
        "ProductId",
        productId);

public class ProductIdBadRequestException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId);

public class ProductNotInOrderBadRequestException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId,
        $"The product with id {productId} does not exist in the order.");

public class DuplicateProductInOrderDomainException(Guid productId)
    : DomainException(
        "ProductId",
        productId,
        $"The product with id {productId} is already added to the order.");
