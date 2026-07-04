namespace Basket.Function.Modules.ShoppingCarts.Data;

public class ShoppingCartNotFoundException(string ownerId)
    : NotFoundException("Basket", ownerId);
