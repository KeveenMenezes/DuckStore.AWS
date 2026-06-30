using BuildingBlocks.Core.DomainModel;

namespace Basket.Function.Models;

public class ShoppingCart : Aggregate<string>
{
    private readonly List<ShoppingCartItem> _items = [];

    private ShoppingCart(string userName) => Id = userName;

    // UserName is the cart's identity (the `shopping-carts` partition key); it mirrors the aggregate Id.
    public string UserName => Id;

    public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();

    public decimal TotalPrice => _items.Sum(item => item.Price * item.Quantity);

    public static ShoppingCart Create(string userName, IEnumerable<ShoppingCartItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var cart = new ShoppingCart(userName);
        cart._items.AddRange(items);

        return cart;
    }

    // Reconstitutes a persisted cart.
    public static ShoppingCart Load(string userName, IEnumerable<ShoppingCartItem> items)
    {
        var cart = new ShoppingCart(userName);
        cart._items.AddRange(items);

        return cart;
    }

    // Cart business rule: each product's coupon reduces the matching item's price.
    // This used to be a per-item cross-service Lambda call to Discount.
    public void ApplyDiscounts(IEnumerable<Coupon> coupons)
    {
        var couponsByProduct = coupons.ToDictionary(coupon => coupon.Id);

        foreach (var item in _items)
        {
            if (couponsByProduct.TryGetValue(item.ProductName, out var coupon))
                item.ApplyDiscount(coupon.Amount);
        }
    }
}
