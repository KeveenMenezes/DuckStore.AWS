using Review.Function.Modules.Reviews.Data;

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
}
