namespace Ordering.FunctionalTests;

public class OrderingApiTests(OrderingApiFixture fixture) : IClassFixture<OrderingApiFixture>
{
    private readonly HttpClient _httpClient = fixture.HttpClient;

    [Fact]
    public async Task HealthEndpoint_Should_ReturnSuccess()
    {
        // Act
        var response = await _httpClient.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_Should_ReturnOrdersList()
    {
        // Act
        var response = await _httpClient.GetAsync("/orders");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(content));
    }

    [Fact]
    public async Task GetOrderById_Should_ReturnOrderDetails()
    {
        // Arrange
        var orderId = 1;

        // Act
        var response = await _httpClient.GetAsync($"/orders/{orderId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(content));
    }

    [Fact]
    public async Task DeleteOrder_Should_ReturnNoContent()
    {
        // Arrange
        var orderId = "194ea999-cd0b-498d-9760-dddf0d74cd2f";

        // Act
        var response = await _httpClient.DeleteAsync($"/orders/{orderId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}


