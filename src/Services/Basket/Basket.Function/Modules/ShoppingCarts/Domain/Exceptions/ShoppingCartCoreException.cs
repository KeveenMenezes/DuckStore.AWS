namespace Basket.Function.Modules.ShoppingCarts.Domain.Exceptions;

public class OwnerIdBadRequestException(string ownerId)
    : BadRequestException(
        "OwnerId",
        ownerId);

public class ProductIdBadRequestException(Guid productId)
    : BadRequestException(
        "ProductId",
        productId);
