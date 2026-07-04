using Amazon.Lambda.DynamoDBEvents;
using Review.Function.Modules.Reviews.EventsIntegration.Publishers;

namespace Review.UnitTests.EventsIntegration;

public class ReviewStreamImageTests
{
    [Fact]
    public void From_ParsesIdProductIdAndRating()
    {
        var reviewId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var image = new Dictionary<string, DynamoDBEvent.AttributeValue>
        {
            ["Id"] = new() { S = reviewId.ToString() },
            ["ProductId"] = new() { S = productId.ToString() },
            ["Rating"] = new() { N = "4" }
        };

        var result = ReviewStreamImage.From(image);

        Assert.Equal(reviewId, result.Id);
        Assert.Equal(productId, result.ProductId);
        Assert.Equal(4, result.Rating);
    }
}
