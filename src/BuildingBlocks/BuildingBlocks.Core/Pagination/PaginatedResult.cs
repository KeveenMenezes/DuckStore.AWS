namespace BuildingBlocks.Core.Pagination;

/// <summary>
/// Represents a paginated result of a query.
/// </summary>
/// <typeparam name="TEntity">The type of entity returned in the pagination.</typeparam>
public class PaginatedResult<TEntity>(
    long pageNumber,
    long pageSize,
    long totalItemCount,
    IEnumerable<TEntity> items)
    : PagedList(
        pageNumber,
        pageSize,
        totalItemCount)
    where TEntity : class
{
    /// <summary>
    /// Items on the current page.
    /// </summary>
    public IEnumerable<TEntity> Items { get; } = items;
}
