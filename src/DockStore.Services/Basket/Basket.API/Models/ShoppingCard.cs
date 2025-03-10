namespace Basket.API.Models;

public class ShoppingCard
{
    public ShoppingCard(string userName)
    {
        UserName = userName;
    }

    //Required for Mapping
    public ShoppingCard()
    {
    }

    public string UserName { get; set; }
    public List<ShopppingClassItem> Items { get; set; } = [];
    public decimal TotalPrice => Items.Sum(x => x.Price * x.Qantity);
}
