namespace Basket.API.Data;

public class BasketRepository : IBasketRepository
{
    public Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
