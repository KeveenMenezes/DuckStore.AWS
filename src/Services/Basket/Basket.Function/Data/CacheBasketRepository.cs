namespace Basket.Function.Data;

public class CacheBasketRepository(
    IBasketRepository basketRepository,
    IConnectionMultiplexer distributedCache)
    : IBasketRepository
{
    private readonly IDatabase _database = distributedCache.GetDatabase();

    public async Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken)
    {
        var cached = await _database.StringGetAsync(userName);

        if (cached.HasValue)
        {
            return BasketSerializer.Deserialize(cached!);
        }

        var basket = await basketRepository.GetBasket(userName, cancellationToken);
        await _database.StringSetAsync(userName, BasketSerializer.Serialize(basket));

        return basket;
    }

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        await basketRepository.StoreCart(cart, cancellationToken);

        await _database.StringSetAsync(cart.UserName, BasketSerializer.Serialize(cart));

        return cart;
    }

    public async Task DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        await basketRepository.DeleteBasket(userName, cancellationToken);

        await _database.KeyDeleteAsync(userName);
    }

    public Task MarkCheckoutAsync(string userName, string checkoutDataJson, CancellationToken cancellationToken) =>
        basketRepository.MarkCheckoutAsync(userName, checkoutDataJson, cancellationToken);
}
