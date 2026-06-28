namespace Basket.Function.Data;

public interface IBasketRepository
{
    Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken);
    Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken);
    Task DeleteBasket(string userName, CancellationToken cancellationToken);
    Task MarkCheckoutAsync(string userName, string checkoutDataJson, CancellationToken cancellationToken);
}
