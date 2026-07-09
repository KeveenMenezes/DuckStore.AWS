using Mapster;
using Catalog.Function.Modules.Categories.Features.CreateCategory;

namespace Catalog.Function;

public record CreateCategoryRequest(string Name, Guid? ParentId);
public record CreateCategoryResponse(Guid Id);

// Not yet wired to an AppSync Mutation data source — Query.categories already covers reads
// (direct DynamoDB resolver per ADR-0009); wiring Mutation.createCategory is a follow-up.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<CreateCategoryResponse> CreateCategory(
        CreateCategoryRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<CreateCategoryCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<CreateCategoryResponse>();
    }
}
