namespace Basket.API.Data;

public interface IBasketRepository
{
    Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken);
    Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken);
    Task<bool> DeleteBasket(string userName, CancellationToken cancellationToken);
}
