using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;

namespace CatalogView.UnitTests.Products;

public class DynamoProductIndexTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoProductIndex _index;

    public DynamoProductIndexTests()
    {
        _index = new DynamoProductIndex(_dynamoDb.Object);
    }

    [Fact]
    public async Task UpsertAsync_OnlySendsProductFields_NeverPriceOrRating()
    {
        UpdateItemRequest? captured = null;
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new UpdateItemResponse());

        var document = new SearchDocument
        {
            Id = "product-1",
            Name = "Debug Duck",
            Description = "A duck",
            ImageUrl = "/duck.jpg",
            Stock = 10,
            CategoryIds = ["cat-1"],
            Categories = [new CategoryRef("cat-1", "Languages")]
        };

        await _index.UpsertAsync(document);

        Assert.NotNull(captured);
        Assert.Equal(DynamoProductIndex.TableName, captured!.TableName);
        Assert.Contains("#Name", captured.UpdateExpression);
        Assert.Contains("#Description", captured.UpdateExpression);
        Assert.Contains("Stock", captured.UpdateExpression);
        Assert.Contains("CategoryIds", captured.UpdateExpression);
        Assert.Contains("Categories", captured.UpdateExpression);

        // Never touches price/rating fields — those are owned by ApplyPricingAsync/ApplyRatingAsync.
        Assert.DoesNotContain("Price", captured.UpdateExpression);
        Assert.DoesNotContain("Rating", captured.UpdateExpression);
    }

    [Fact]
    public async Task ApplyRatingAsync_ReplayingSameEventId_IsANoOp()
    {
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(
                It.Is<UpdateItemRequest>(r => r.ConditionExpression != null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("already applied"));

        // Should not throw — a replayed eventId is treated as an idempotent no-op.
        await _index.ApplyRatingAsync("product-1", "event-1", 5);

        // The recompute-average step (a second UpdateItem, without a ConditionExpression) must
        // never run, since the conditional ADD never actually applied.
        _dynamoDb.Verify(
            d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _dynamoDb.Verify(
            d => d.UpdateItemAsync(
                It.Is<UpdateItemRequest>(r => r.ConditionExpression == null),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyRatingAsync_NewEventId_AccumulatesAndRecomputesAverage()
    {
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(
                It.Is<UpdateItemRequest>(r => r.ConditionExpression != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse());

        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["RatingSum"] = new AttributeValue { N = "9" },
                    ["RatingCount"] = new AttributeValue { N = "2" }
                }
            });

        UpdateItemRequest? recomputeRequest = null;
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(
                It.Is<UpdateItemRequest>(r => r.ConditionExpression == null),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((r, _) => recomputeRequest = r)
            .ReturnsAsync(new UpdateItemResponse());

        await _index.ApplyRatingAsync("product-1", "event-2", 4);

        Assert.NotNull(recomputeRequest);
        Assert.Equal("4.5", recomputeRequest!.ExpressionAttributeValues[":average"].N);
    }

    [Fact]
    public async Task RenameCategoryAsync_UpdatesMatchingCategoryName_AcrossReturnedScanItems()
    {
        var scannedItem = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new("product-1"),
            ["Categories"] = new AttributeValue
            {
                L =
                [
                    new AttributeValue
                    {
                        M = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new("cat-other"),
                            ["Name"] = new("Other")
                        }
                    },
                    new AttributeValue
                    {
                        M = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new("cat-1"),
                            ["Name"] = new("Languages")
                        }
                    }
                ]
            }
        };

        _dynamoDb
            .Setup(d => d.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResponse { Items = [scannedItem], LastEvaluatedKey = null });

        UpdateItemRequest? captured = null;
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new UpdateItemResponse());

        await _index.RenameCategoryAsync("cat-1", "Programming Languages");

        Assert.NotNull(captured);
        // Second entry (index 1) is the one whose Id matches "cat-1".
        Assert.Equal("SET Categories[1].#Name = :name", captured!.UpdateExpression);
        Assert.Equal("Programming Languages", captured.ExpressionAttributeValues[":name"].S);
    }
}
