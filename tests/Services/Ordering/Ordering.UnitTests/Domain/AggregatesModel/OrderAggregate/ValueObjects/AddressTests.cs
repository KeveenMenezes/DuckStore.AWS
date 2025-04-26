namespace Ordering.UnitTests.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public class AddressTests
{
    [Fact]
    public void Of_ShouldCreateAddressWithValidData()
    {
        // Arrange
        var firstName = "John";
        var lastName = "Doe";
        var emailAddress = "john.doe@example.com";
        var addressLine = "123 Main St";
        var country = "USA";
        var state = "CA";
        var zipCode = "90001";

        // Act
        var address = Address.Of(firstName, lastName, emailAddress, addressLine, country, state, zipCode);

        // Assert
        Assert.NotNull(address);
        Assert.Equal(firstName, address.FirstName);
        Assert.Equal(lastName, address.LastName);
        Assert.Equal(emailAddress, address.EmailAddress);
        Assert.Equal(addressLine, address.AddressLine);
        Assert.Equal(country, address.Country);
        Assert.Equal(state, address.State);
        Assert.Equal(zipCode, address.ZipCode);
    }

    [Fact]
    public void Of_ShouldThrowException_WhenEmailAddressIsNullOrWhiteSpace()
    {
        // Arrange
        var firstName = "John";
        var lastName = "Doe";
        var emailAddress = " ";
        var addressLine = "123 Main St";
        var country = "USA";
        var state = "CA";
        var zipCode = "90001";

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Address.Of(firstName, lastName, emailAddress, addressLine, country, state, zipCode));
    }

    [Fact]
    public void Of_ShouldThrowException_WhenAddressLineIsNullOrWhiteSpace()
    {
        // Arrange
        var firstName = "John";
        var lastName = "Doe";
        var emailAddress = "john.doe@example.com";
        var addressLine = " ";
        var country = "USA";
        var state = "CA";
        var zipCode = "90001";

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Address.Of(firstName, lastName, emailAddress, addressLine, country, state, zipCode));
    }
}
