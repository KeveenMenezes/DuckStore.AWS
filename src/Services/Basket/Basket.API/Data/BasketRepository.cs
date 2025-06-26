namespace Basket.API.Data;

public class BasketRepository(IDocumentSession session)
    : IBasketRepository
{
    public async Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var basket = await session.LoadAsync<ShoppingCart>(userName, cancellationToken) ??
            throw new BasketNotFoundException(userName);

        return basket;
    }

    public Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        session.Store(cart);
        return Task.FromResult(cart);
    }

    public Task DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        session.Delete<ShoppingCart>(userName);
        return Task.CompletedTask;
    }
}
