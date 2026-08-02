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

    // The cart path: one BatchGetItem for the whole basket instead of a GetItem per line.
    [Fact]
    public async Task GetActiveDiscountsForProductsAsync_ShouldReadEveryProductInOneBatch()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var requests = CaptureBatchGets(BatchResponse(ActiveRow(first), ActiveRow(second)));

        var result = await _repository.GetActiveDiscountsForProductsAsync([first, second]);

        var request = Assert.Single(requests);
        Assert.Equal(2, request.RequestItems["product-discounts"].Keys.Count);
        Assert.Equal(2, result.Count);
    }

    // A repeated product is one line item twice, not two products: the extra key would be a read
    // charged for nothing.
    [Fact]
    public async Task GetActiveDiscountsForProductsAsync_ShouldRequestEachProductOnce_WhenTheSameOneRepeats()
    {
        var productId = Guid.NewGuid();
        var requests = CaptureBatchGets(BatchResponse(ActiveRow(productId)));

        await _repository.GetActiveDiscountsForProductsAsync([productId, productId, productId]);

        var request = Assert.Single(requests);
        Assert.Single(request.RequestItems["product-discounts"].Keys);
    }

    // Same read-time expiry rule as the single-product read: an expired row is indistinguishable
    // from no row, so the cart quotes the full price rather than an ended campaign's.
    [Fact]
    public async Task GetActiveDiscountsForProductsAsync_ShouldOmitProducts_WhoseDiscountHasExpired()
    {
        var active = Guid.NewGuid();
        var expired = Guid.NewGuid();
        CaptureBatchGets(BatchResponse(ActiveRow(active), ExpiredRow(expired)));

        var result = await _repository.GetActiveDiscountsForProductsAsync([active, expired]);

        Assert.True(result.ContainsKey(active));
        Assert.False(result.ContainsKey(expired));
    }

    // UnprocessedKeys under throttling must be retried: dropping them would quote the customer the
    // undiscounted price while the product page still advertises the campaign.
    [Fact]
    public async Task GetActiveDiscountsForProductsAsync_ShouldRetryUnprocessedKeys()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var throttled = new Dictionary<string, KeysAndAttributes>
        {
            ["product-discounts"] = new()
            {
                Keys = [new Dictionary<string, AttributeValue> { ["ProductId"] = new(second.ToString()) }]
            }
        };

        _dynamoDb
            .SetupSequence(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
                {
                    ["product-discounts"] = [ActiveRow(first)]
                },
                UnprocessedKeys = throttled
            })
            .ReturnsAsync(BatchResponse(ActiveRow(second)));

        var result = await _repository.GetActiveDiscountsForProductsAsync([first, second]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetActiveDiscountsForProductsAsync_ShouldNotCallDynamo_WhenTheBasketIsEmpty()
    {
        var result = await _repository.GetActiveDiscountsForProductsAsync([]);

        Assert.Empty(result);
        _dynamoDb.Verify(
            d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private List<BatchGetItemRequest> CaptureBatchGets(BatchGetItemResponse response)
    {
        var requests = new List<BatchGetItemRequest>();
        _dynamoDb
            .Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BatchGetItemRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(response);
        return requests;
    }

    private static BatchGetItemResponse BatchResponse(params Dictionary<string, AttributeValue>[] items) =>
        new()
        {
            Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
            {
                ["product-discounts"] = [.. items]
            },
            UnprocessedKeys = []
        };

    private static Dictionary<string, AttributeValue> ActiveRow(Guid productId) =>
        Row(productId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

    private static Dictionary<string, AttributeValue> ExpiredRow(Guid productId) =>
        Row(productId, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-1));

    private static Dictionary<string, AttributeValue> Row(Guid productId, DateTime startsAt, DateTime endsAt) =>
        new()
        {
            ["ProductId"] = new(productId.ToString()),
            ["DiscountType"] = new(nameof(DiscountType.Percentage)),
            ["Value"] = new AttributeValue { N = "10" },
            ["StartsAt"] = new(startsAt.ToString("o")),
            ["EndsAt"] = new(endsAt.ToString("o"))
        };
}
