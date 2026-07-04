namespace Basket.Function.Modules.ShoppingCarts.Domain.Entities;

public class ShoppingCart : Aggregate<OwnerId>
{
    private readonly List<ShoppingCartItem> _items = [];

    private ShoppingCart(OwnerId ownerId) => Id = ownerId;

    // OwnerId is the cart's identity (the `shopping-carts` partition key); it mirrors the aggregate Id.
    // Prefixed by the caller as USER#<cognito-sub> (authenticated) or GUEST#<guestId> (visitor).
    public string OwnerId => Id.Value;

    // True for visitor carts, which the DynamoDB TTL is allowed to expire.
    public bool IsGuest => Id.IsGuest;

    public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();

    public decimal TotalPrice => _items.Sum(item => item.Price * item.Quantity);

    public static ShoppingCart Create(string ownerId, IEnumerable<ShoppingCartItem> items)
    {
        var cart = new ShoppingCart(Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects.OwnerId.Of(ownerId));
        cart._items.AddRange(items);

        return cart;
    }

    // Reconstitutes a persisted cart (skips re-validation).
    public static ShoppingCart Load(string ownerId, IEnumerable<ShoppingCartItem> items)
    {
        var cart = new ShoppingCart(Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects.OwnerId.Of(ownerId));
        cart._items.AddRange(items);

        return cart;
    }

    // Merges another cart's items into this one, combining quantities of the same product
    // (matched by ProductId + Color) so logging in doesn't duplicate lines.
    public void Merge(ShoppingCart other)
    {
        foreach (var incoming in other._items)
        {
            var existing = _items.FirstOrDefault(i =>
                i.ProductId == incoming.ProductId && i.Color == incoming.Color);

            if (existing is null)
            {
                _items.Add(incoming);
                continue;
            }

            _items.Remove(existing);
            _items.Add(ShoppingCartItem.Load(
                existing.ProductId.Value, existing.ProductName, existing.ImageUrl, existing.Color,
                existing.Quantity + incoming.Quantity, existing.Price));
        }
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
