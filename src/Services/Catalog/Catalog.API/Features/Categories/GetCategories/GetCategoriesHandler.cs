namespace Catalog.API.Features.Categories.GetCategories;

public record GetCategoriesQuery(int PageIndex, int PageSize)
    : IQuery<GetCategoriesResult>;

public record GetCategoriesResult(PaginatedResult<Category> PaginatedCategories);

public class GetCategoriesQueryHandler(IDocumentSession session)
    : IQueryHandler<GetCategoriesQuery, GetCategoriesResult>
{
    public async Task<GetCategoriesResult> Handle(
        GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var pagedList = await session
            .Query<Category>()
            .ToPagedListAsync(
                request.PageIndex,
                request.PageSize,
                cancellationToken);

        return new GetCategoriesResult(
            new PaginatedResult<Category>(
                pagedList.PageNumber,
                pagedList.PageSize,
                pagedList.TotalItemCount,
                pagedList.AsEnumerable())
        );
    }
}
