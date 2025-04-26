namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class ProductIdTests
{
    [Fact]
    public void Of_ShouldCreateProductIdWithValidGuid()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var productId = ProductId.Of(validGuid);

        // Assert
        Assert.NotNull(productId);
        Assert.Equal(validGuid, productId.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenGuidIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act & Assert
        Assert.Throws<ProductIdBadRequestException>(() => ProductId.Of(emptyGuid));
    }
}
