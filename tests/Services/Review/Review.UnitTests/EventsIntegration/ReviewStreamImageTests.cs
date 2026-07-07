using Amazon.Lambda.DynamoDBEvents;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;

namespace Review.UnitTests.EventsIntegration;

public class ReviewStreamImageTests
{
    [Fact]
    public void From_ParsesFullImage()
    {
        var productId = Guid.NewGuid();

        var image = new Dictionary<string, DynamoDBEvent.AttributeValue>
        {
            ["Id"] = new() { S = $"{productId}#dXNlcg==" },
            ["ProductId"] = new() { S = productId.ToString() },
            ["UserName"] = new() { S = "user" },
            ["Comment"] = new() { S = "Great product" },
            ["Rating"] = new() { N = "4" },
            ["CreatedAt"] = new() { S = "2026-01-01T00:00:00.000Z" },
            ["UpdatedAt"] = new() { S = "2026-01-02T00:00:00.000Z" }
        };

        var result = ReviewStreamImage.From(image);

        Assert.NotNull(result);
        Assert.Equal($"{productId}#dXNlcg==", result.Id);
        Assert.Equal(productId, result.ProductId);
        Assert.Equal("user", result.UserName);
        Assert.Equal("Great product", result.Comment);
        Assert.Equal(4, result.Rating);
        Assert.Equal("2026-01-01T00:00:00.000Z", result.CreatedAt);
        Assert.Equal("2026-01-02T00:00:00.000Z", result.UpdatedAt);
    }

    [Fact]
    public void From_NullImage_ReturnsNull()
    {
        Assert.Null(ReviewStreamImage.From(null));
    }

    [Fact]
    public void From_EmptyImage_ReturnsNull()
    {
        Assert.Null(ReviewStreamImage.From([]));
    }

    [Fact]
    public void From_RemoveShapedImage_WithOnlyOldImage_DoesNotThrow()
    {
        // REMOVE stream records only carry OldImage — TryGetValue-based parsing must not throw,
        // unlike the old unsafe indexer access this replaces.
        var image = new Dictionary<string, DynamoDBEvent.AttributeValue>
        {
            ["Id"] = new() { S = "some-id" }
        };

        var result = ReviewStreamImage.From(image);

        Assert.NotNull(result);
        Assert.Equal("some-id", result.Id);
        Assert.Equal(Guid.Empty, result.ProductId);
        Assert.Equal(string.Empty, result.UserName);
        Assert.Equal(0, result.Rating);
    }
}
