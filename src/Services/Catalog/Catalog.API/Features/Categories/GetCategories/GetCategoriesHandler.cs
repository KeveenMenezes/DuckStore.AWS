namespace Catalog.API.Features.Categories.GetCategories;

public record GetCategoriesQuery(int PageIndex, int PageSize)
    : IQuery<GetCategoriesResult>;

public record GetCategoriesResult(PaginatedResult<Category> PaginatedCategories);

public class GetCategoriesQueryHandler(ICategoryRepository categoryRepository)
    : IQueryHandler<GetCategoriesQuery, GetCategoriesResult>
{
    public async Task<GetCategoriesResult> Handle(
        GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var pagedCategories = await categoryRepository.GetPagedAsync(
            request.PageIndex,
            request.PageSize,
            cancellationToken);

        return new GetCategoriesResult(pagedCategories);
    }
}
