
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

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        session.Store(cart);
        await session.SaveChangesAsync(cancellationToken);

        return cart;
    }

    public async Task DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        session.Delete<ShoppingCart>(userName);
        await session.SaveChangesAsync(cancellationToken);
    }
}
