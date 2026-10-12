namespace Basket.Function.Modules.ShoppingCarts.Data;

public interface IShoppingCartRepository
{
    Task<ShoppingCart> GetBasket(string ownerId, CancellationToken cancellationToken);

    // Returns null instead of throwing when the cart is absent (used by the login merge).
    Task<ShoppingCart?> TryGetBasket(string ownerId, CancellationToken cancellationToken);
    Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken);
    Task DeleteBasket(string ownerId, CancellationToken cancellationToken);
    Task MarkCheckoutAsync(string ownerId, string checkoutDataJson, CancellationToken cancellationToken);

    // Closes the cart for one checkout attempt in a single conditional transaction (ADR-0050 §4).
    // cartVersion is the Version the saga read; null for a cart written before carts were versioned.
    Task<CartCloseResult> CloseCartAsync(
        string ownerId, string checkoutId, string? cartVersion, Guid orderId, string checkoutDataJson,
        CancellationToken cancellationToken);

    // Idempotently moves the guest cart into the user cart and deletes the guest cart.
    Task MergeAsync(ShoppingCart mergedUserCart, string guestOwnerId, CancellationToken cancellationToken);
}
