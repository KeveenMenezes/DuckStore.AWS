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

    [Fact]
    public void EffectiveStatus_UnknownStatus_Throws() =>
        Assert.Throws<ArgumentException>(() => ReviewSchema.EffectiveStatus("Archived"));
}
