using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Pricing.UnitTests.DataTests;

public class DynamoCampaignRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoCampaignRepository _repository;

    public DynamoCampaignRepositoryTests()
    {
        _repository = new DynamoCampaignRepository(_dynamoDb.Object);
    }

    [Fact]
    public async Task AddAsync_ShouldWriteOneCampaignItem_PlusOneProductDiscountItemPerProduct()
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Percentage, 20),
            new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            [ProductId.Of(Guid.NewGuid()), ProductId.Of(Guid.NewGuid()), ProductId.Of(Guid.NewGuid())]);

        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.AddAsync(campaign);

        Assert.NotNull(captured);
        // 1 campaign item + 3 product-discounts items.
        Assert.Equal(4, captured!.TransactItems.Count);
        Assert.Equal("campaigns", captured.TransactItems[0].Put.TableName);
        Assert.All(captured.TransactItems.Skip(1), item =>
            Assert.Equal("product-discounts", item.Put.TableName));
    }

    [Fact]
    public async Task CancelAsync_ShouldUpdateCampaignStatus_AndDeleteEachProductDiscountRow()
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Fixed, 5),
            new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            [ProductId.Of(Guid.NewGuid()), ProductId.Of(Guid.NewGuid())]);

        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.CancelAsync(campaign);

        Assert.NotNull(captured);
        // 1 campaign status update + 2 product-discounts deletes.
        Assert.Equal(3, captured!.TransactItems.Count);
        Assert.Equal("campaigns", captured.TransactItems[0].Update.TableName);
        Assert.All(captured.TransactItems.Skip(1), item =>
            Assert.Equal("product-discounts", item.Delete.TableName));
    }

    [Fact]
    public async Task GetActiveDiscountForProductAsync_ShouldReturnNull_WhenProductHasNoDiscountRow()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });

        var result = await _repository.GetActiveDiscountForProductAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveDiscountForProductAsync_ShouldReturnNull_WhenDiscountHasExpired()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["ProductId"] = new(Guid.NewGuid().ToString()),
                    ["DiscountType"] = new(nameof(DiscountType.Percentage)),
                    ["Value"] = new AttributeValue { N = "10" },
                    ["StartsAt"] = new(DateTime.UtcNow.AddDays(-10).ToString("o")),
                    ["EndsAt"] = new(DateTime.UtcNow.AddDays(-1).ToString("o"))
                }
            });

        var result = await _repository.GetActiveDiscountForProductAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveDiscountForProductAsync_ShouldReturnDiscount_WhenActive()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["ProductId"] = new(Guid.NewGuid().ToString()),
                    ["DiscountType"] = new(nameof(DiscountType.Fixed)),
                    ["Value"] = new AttributeValue { N = "5" },
                    ["StartsAt"] = new(DateTime.UtcNow.AddDays(-1).ToString("o")),
                    ["EndsAt"] = new(DateTime.UtcNow.AddDays(1).ToString("o"))
                }
            });

        var result = await _repository.GetActiveDiscountForProductAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(DiscountType.Fixed, result!.Type);
        Assert.Equal(5m, result.Amount);
    }
}
