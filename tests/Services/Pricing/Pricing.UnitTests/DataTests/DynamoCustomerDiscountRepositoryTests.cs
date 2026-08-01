using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Pricing.UnitTests.DataTests;

public class DynamoCustomerDiscountRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoCustomerDiscountRepository _repository;

    public DynamoCustomerDiscountRepositoryTests()
    {
        _repository = new DynamoCustomerDiscountRepository(_dynamoDb.Object);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenNoItemExists()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });

        var result = await _repository.GetAsync("USER#alice", "discount-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ShouldMapTheStoredItem()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OwnerId"] = new("USER#alice"),
                    ["DiscountId"] = new("discount-1"),
                    ["Amount"] = new AttributeValue { N = "50" },
                    ["Status"] = new("Issued"),
                    ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToUnixTimeSeconds().ToString() },
                    ["SourceRedemptionId"] = new("redemption-1"),
                    ["CreatedAt"] = new(DateTime.UtcNow.ToString("O"))
                }
            });

        var result = await _repository.GetAsync("USER#alice", "discount-1");

        Assert.NotNull(result);
        Assert.Equal("discount-1", result!.Id);
        Assert.Equal(50m, result.Amount);
        Assert.Equal(CustomerDiscountStatus.Issued, result.Status);
        Assert.Equal(expiresAt.UtcDateTime, result.ExpiresAt);
    }
}
