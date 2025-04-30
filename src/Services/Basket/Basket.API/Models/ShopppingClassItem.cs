namespace Basket.API.Models;

public class ShopppingClassItem
{
    public int Quantity { get; set; }
    public required string Color { get; set; }
    public decimal Price { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }
}



