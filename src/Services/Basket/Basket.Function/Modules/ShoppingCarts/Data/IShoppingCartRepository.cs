namespace Basket.Function.Modules.ShoppingCarts.Data;

public interface IShoppingCartRepository
{
    Task<ShoppingCart> GetBasket(string ownerId, CancellationToken cancellationToken);

    // Returns null instead of throwing when the cart is absent (used by the login merge).
    Task<ShoppingCart?> TryGetBasket(string ownerId, CancellationToken cancellationToken);
    Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken);
    Task DeleteBasket(string ownerId, CancellationToken cancellationToken);
    Task MarkCheckoutAsync(string ownerId, string checkoutDataJson, CancellationToken cancellationToken);

    // Idempotently moves the guest cart into the user cart and deletes the guest cart.
    Task MergeAsync(ShoppingCart mergedUserCart, string guestOwnerId, CancellationToken cancellationToken);
}
