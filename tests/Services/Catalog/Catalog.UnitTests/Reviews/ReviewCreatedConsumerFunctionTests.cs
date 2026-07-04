using Catalog.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;

namespace Catalog.UnitTests.Reviews;

public class ReviewCreatedConsumerFunctionTests
{
    [Fact]
    public void CalculateAverage_ReturnsZero_WhenNoRatings()
    {
        var average = ReviewCreatedHandler.CalculateAverage(ratingSum: 0, ratingCount: 0);

        Assert.Equal(0, average);
    }

    [Theory]
    [InlineData(5, 1, 5)]
    [InlineData(9, 2, 4.5)]
    [InlineData(10, 4, 2.5)]
    public void CalculateAverage_DividesSumByCount(long sum, long count, double expected)
    {
        var average = ReviewCreatedHandler.CalculateAverage(sum, count);

        Assert.Equal(expected, average);
    }
}
