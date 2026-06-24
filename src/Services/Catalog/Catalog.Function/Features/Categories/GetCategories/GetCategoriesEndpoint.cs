using Catalog.API.Features.Categories.GetCategories;

namespace Catalog.Function;

public partial class Functions
{
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/categories")]
    public async Task<PaginatedResult<Category>> GetCategories(
        [FromServices] ISender sender,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await sender.Send(new GetCategoriesQuery(pageIndex, pageSize), CancellationToken.None);

        return result.PaginatedCategories;
    }
}
