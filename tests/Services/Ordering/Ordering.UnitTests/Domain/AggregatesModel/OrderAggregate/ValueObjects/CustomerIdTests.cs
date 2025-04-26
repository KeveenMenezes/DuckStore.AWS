namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class CustomerIdTests
{
    [Fact]
    public void Of_ShouldCreateCustomerIdWithValidGuid()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var customerId = CustomerId.Of(validGuid);

        // Assert
        Assert.NotNull(customerId);
        Assert.Equal(validGuid, customerId.Value);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenGuidIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act & Assert
        Assert.Throws<CustomerIdBadRequestException>(() => CustomerId.Of(emptyGuid));
    }
}
