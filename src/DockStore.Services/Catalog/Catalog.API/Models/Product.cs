namespace Catalog.API.Models;

public class Product(
    Guid id,
    string name,
    string description,
    string imageUrl,
    decimal price,
    List<string> categories)
{
    public Guid Id { get; private set; } = id;
    public string Name { get; private set; } = name;
    public string Description { get; private set; } = description;
    public string ImageUrl { get; private set; } = imageUrl;
    public decimal Price { get; private set; } = price;
    public List<string> Categories { get; private set; } = categories;

    internal void Update(
        string name,
        string description,
        string imageUrl,
        decimal price,
        List<string> categories)
    {
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price;
        Categories = categories;
    }
}



