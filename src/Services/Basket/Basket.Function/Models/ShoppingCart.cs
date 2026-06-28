namespace Basket.Function.Models;

public class ShoppingCart
{
    public ShoppingCart(string userName)
    {
        UserName = userName;
    }

    //Required for Mapping
    public ShoppingCart()
    {
    }

    public string UserName { get; init; }

    public List<ShopppingClassItem> Items { get; init; } = [];

    public decimal TotalPrice => Items.Sum(x => x.Price * x.Quantity);
}
