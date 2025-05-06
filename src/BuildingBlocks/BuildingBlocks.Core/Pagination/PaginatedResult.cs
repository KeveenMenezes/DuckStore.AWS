namespace BuildingBlocks.Core.Pagination;

public class PaginatedResult<TEntity>
    (int pageIndex, int pageSize, long count, IAsyncEnumerable<TEntity> data)
    where TEntity : class
{
    public int PageIndex { get; } = pageIndex;
    public int PageSize { get; } = pageSize;
    public long Count { get; } = count;
    public IAsyncEnumerable<TEntity> Data { get; } = data;
}
