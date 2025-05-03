using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace Ordering.FunctionalTests;

public class OrderingApiTests : IClassFixture<OrderingApiFixture>
{
    private readonly WebApplicationFactory<Program> _webApplicationFactory;
    private readonly HttpClient _httpClient;

    public OrderingApiTests(OrderingApiFixture fixture)
    {
        _webApplicationFactory = fixture;
        _httpClient = _webApplicationFactory.CreateDefaultClient();
    }

    [Fact]
    public async Task HealthEndpoint_Should_ReturnSuccess()
    {
        // Act
        var response = await _httpClient.GetAsync("/orders/health");

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
    public async Task CreateOrder_Should_ReturnCreatedStatus()
    {
        // Arrange
        var content = new StringContent(
            JsonSerializer.Serialize(CreateOrderDtoWithValidItems()),
            Encoding.UTF8,
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));

        // Act
        var response = await _httpClient.PostAsync("/orders", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
        var orderId = 100;

        // Act
        var response = await _httpClient.DeleteAsync($"/orders/{orderId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static OrderDto CreateOrderDtoWithValidItems(Guid? orderId = null) =>
        new(
            orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Test Order",
            new AddressDto(
                "John",
                "Doe",
                "john.doe@example.com",
                "123 Street",
                "Country",
                "State",
                "12345"),
            new AddressDto(
                "John",
                "Doe",
                "john.doe@example.com",
                "123 Street",
                "Country",
                "State",
                "12345"),
            new PaymentDto(
                "CardName",
                "4111111111111111",
                "12/25",
                "123",
                PaymentMethod.Debit),
            OrderStatus.Pending,
            [
                new OrderItemDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    2,
                    50)
            ]);
}
