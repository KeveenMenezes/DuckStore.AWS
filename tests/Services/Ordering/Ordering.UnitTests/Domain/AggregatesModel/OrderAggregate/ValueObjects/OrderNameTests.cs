namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class OrderNameTests
{
    [Fact]
    public void Of_ShouldCreateOrderNameWithValidValue()
    {
        // Arrange
        var validName = "Valid Order Name";

        // Act
        var orderName = OrderName.Of(validName);

        // Assert
        Assert.NotNull(orderName);
        Assert.Equal(validName, orderName.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenValueIsNullOrWhiteSpace()
    {
        // Arrange
        var invalidName = " ";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => OrderName.Of(invalidName));
    }
}
