using Mapster;
using Catalog.Function.Modules.Categories.Features.UpdateCategory;

namespace Catalog.Function;

public record UpdateCategoryRequest(Guid CategoryId, string Name, Guid? ParentId);
public record UpdateCategoryResponse(Guid CategoryId, bool Renamed, bool Moved);

// Not yet wired to an AppSync Mutation data source — see CreateCategory/Endpoint.cs.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<UpdateCategoryResponse> UpdateCategory(
        UpdateCategoryRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<UpdateCategoryCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<UpdateCategoryResponse>();
    }
}
