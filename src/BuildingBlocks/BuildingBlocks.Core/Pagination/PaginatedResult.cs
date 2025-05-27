namespace BuildingBlocks.Core.Pagination;

/// <summary>
/// Representa um resultado paginado de uma consulta.
/// </summary>
/// <typeparam name="TEntity">O tipo da entidade retornada na paginação.</typeparam>
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
    /// Itens da página atual.
    /// </summary>
    public IEnumerable<TEntity> Items { get; } = items;
}
