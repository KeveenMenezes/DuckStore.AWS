namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.Models;

public class CustomerTests
{
    [Fact]
    public void Create_ShouldInitializeCustomerWithValidData()
    {
        // Arrange
        var customerId = new CustomerId(Guid.NewGuid());
        var name = "John Doe";
        var email = "john.doe@example.com";

        // Act
        var customer = Customer.Create(customerId, name, email);

        // Assert
        Assert.NotNull(customer);
        Assert.Equal(customerId, customer.Id);
        Assert.Equal(name, customer.Name);
        Assert.Equal(email, customer.Email);
    }

    [Fact]
    public void Create_ShouldThrowException_WhenNameIsNullOrEmpty()
    {
        // Arrange
        var customerId = new CustomerId(Guid.NewGuid());
        var email = "john.doe@example.com";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            Customer.Create(customerId, null!, email));

        Assert.Throws<ArgumentException>(() =>
            Customer.Create(customerId, "", email));
    }

    [Fact]
    public void Create_ShouldThrowException_WhenEmailIsNullOrEmpty()
    {
        // Arrange
        var customerId = new CustomerId(Guid.NewGuid());
        var name = "John Doe";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            Customer.Create(customerId, name, null!));

        Assert.Throws<ArgumentException>(() =>
            Customer.Create(customerId, name, ""));
    }
}
