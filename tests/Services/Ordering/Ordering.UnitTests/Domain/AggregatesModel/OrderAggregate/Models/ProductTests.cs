namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.Models;

public class ProductTests
{
    [Fact]
    public void Create_ShouldInitializeProduct_WithValidData()
    {
        // Arrange
        var productId = ProductId.Of(Guid.NewGuid());
        var name = "Test Product";
        var price = 100m;

        // Act
        var product = Product.Create(productId, name, price);

        // Assert
        Assert.NotNull(product);
        Assert.Equal(productId, product.Id);
        Assert.Equal(name, product.Name);
        Assert.Equal(price, product.Price);
    }

    [Fact]
    public void Create_ShouldThrowException_WhenNameIsNullOrWhiteSpace()
    {
        // Arrange
        var productId = ProductId.Of(Guid.NewGuid());
        var invalidName = "";
        var price = 100m;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Product.Create(productId, invalidName, price));
    }

    [Fact]
    public void Create_ShouldThrowException_WhenPriceIsNegative()
    {
        // Arrange
        var productId = ProductId.Of(Guid.NewGuid());
        var name = "Test Product";
        var invalidPrice = -10m;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Product.Create(productId, name, invalidPrice));
    }

    [Fact]
    public void Create_ShouldThrowException_WhenPriceIsZero()
    {
        // Arrange
        var productId = ProductId.Of(Guid.NewGuid());
        var name = "Test Product";
        var invalidPrice = 0m;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Product.Create(productId, name, invalidPrice));
    }
}
