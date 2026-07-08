using Mapster;
using Catalog.Function.Modules.Categories.Features.DeleteCategory;

namespace Catalog.Function;

public record DeleteCategoryRequest(Guid CategoryId);
public record DeleteCategoryResponse(Guid CategoryId, bool Deleted);

// Not yet wired to an AppSync Mutation data source — see CreateCategory/Endpoint.cs.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<DeleteCategoryResponse> DeleteCategory(
        DeleteCategoryRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<DeleteCategoryCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<DeleteCategoryResponse>();
    }
}
