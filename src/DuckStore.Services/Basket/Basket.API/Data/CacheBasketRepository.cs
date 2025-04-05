namespace Basket.API.Data;

public class CacheBasketRepository(
    IBasketRepository basketRepository,
    IConnectionMultiplexer distributedCache)
    : IBasketRepository
{
    private readonly IDatabase _database = distributedCache.GetDatabase();

    public async Task<ShoppingCart> GetBasket(string userName, CancellationToken cancellationToken)
    {
        using var dataBasket = await _database.StringGetLeaseAsync(userName);

        if (dataBasket is not null)
        {
            return JsonSerializer.Deserialize<ShoppingCart>(dataBasket.Span)!;
        }

        var basket = await basketRepository.GetBasket(userName, cancellationToken);
        await _database.StringSetAsync(userName, JsonSerializer.Serialize(basket));

        return basket;
    }

    public async Task<ShoppingCart> StoreCart(ShoppingCart cart, CancellationToken cancellationToken)
    {
        await basketRepository.StoreCart(cart, cancellationToken);

        await _database.StringSetAsync(
            cart.UserName, JsonSerializer.Serialize(cart));

        return cart;
    }

    public async Task DeleteBasket(string userName, CancellationToken cancellationToken)
    {
        await basketRepository.DeleteBasket(userName, cancellationToken);

        await _database.KeyDeleteAsync(userName);
    }
}
