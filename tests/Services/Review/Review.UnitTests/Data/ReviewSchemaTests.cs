using Review.Function.Modules.Reviews.Data;
using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.UnitTests.Data;

public class ReviewSchemaTests
{
    [Fact]
    public void ComposeId_ReturnsProductIdHashUserId_NoEncoding()
    {
        var productId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        var id = ReviewSchema.ComposeId(productId, userId);

        Assert.Equal($"{productId}#{userId}", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EffectiveStatus_MissingStatus_IsPublished_ForLegacyRows(string? status) =>
        Assert.Equal(ReviewStatus.Published, ReviewSchema.EffectiveStatus(status));

    [Theory]
    [InlineData("Eligible", ReviewStatus.Eligible)]
    [InlineData("Published", ReviewStatus.Published)]
    [InlineData("Deleted", ReviewStatus.Deleted)]
    public void EffectiveStatus_ParsesStoredStatus(string status, ReviewStatus expected) =>
        Assert.Equal(expected, ReviewSchema.EffectiveStatus(status));

    // Enum.Parse would silently accept a numeric string ("7" → an undefined value no rule matches,
    // "1" → Published), so an unexpected Status must fail loudly: the stream source's bisect +
    // retries then park that one record in the DLQ (alarmed) instead of dropping its event.
    [Theory]
    [InlineData("Archived")]
    [InlineData("published")]
    [InlineData("1")]
    [InlineData("7")]
    public void EffectiveStatus_UnknownStatus_ThrowsNamingTheValue(string status)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ReviewSchema.EffectiveStatus(status));

        Assert.Contains($"\"{status}\"", ex.Message);
    }
}
