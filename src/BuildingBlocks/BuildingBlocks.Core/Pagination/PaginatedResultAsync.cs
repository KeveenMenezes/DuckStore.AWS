namespace BuildingBlocks.Core.Pagination;

/// <summary>
/// Representa um resultado paginado de uma consulta.
/// </summary>
/// <typeparam name="TEntity">O tipo da entidade retornada na paginação.</typeparam>
public class PaginatedResultAsync<TEntity>(
    long pageNumber,
    long pageSize,
    long totalItemCount,
    IAsyncEnumerable<TEntity> items)
    : PagedList(pageNumber, pageSize, totalItemCount)
    where TEntity : class
{
    public IAsyncEnumerable<TEntity> Items { get; } = items;
}
