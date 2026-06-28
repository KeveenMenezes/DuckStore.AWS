using Catalog.Function.EventsIntegration.Consumer;

namespace Catalog.UnitTests.Reviews;

public class ReviewCreatedConsumerFunctionTests
{
    [Fact]
    public void CalculateAverage_ReturnsZero_WhenNoRatings()
    {
        var average = ReviewCreatedConsumerFunction.CalculateAverage(ratingSum: 0, ratingCount: 0);

        Assert.Equal(0, average);
    }

    [Theory]
    [InlineData(5, 1, 5)]      // a single 5-star review
    [InlineData(9, 2, 4.5)]    // 5 + 4 over two reviews
    [InlineData(10, 4, 2.5)]   // averages to a fractional value
    public void CalculateAverage_DividesSumByCount(long sum, long count, double expected)
    {
        var average = ReviewCreatedConsumerFunction.CalculateAverage(sum, count);

        Assert.Equal(expected, average);
    }
}
