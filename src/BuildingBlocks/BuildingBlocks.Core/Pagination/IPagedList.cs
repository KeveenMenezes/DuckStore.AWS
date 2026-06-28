namespace BuildingBlocks.Core.Pagination;

public class PagedList(
    long pageNumber,
    long pageSize,
    long totalItemCount
)
{
    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public long PageNumber { get; } = pageNumber;

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public long PageSize { get; } = pageSize;

    /// <summary>
    /// Total number of items in the query.
    /// </summary>
    public long TotalItemCount { get; } = totalItemCount;

    /// <summary>
    /// Total number of available pages.
    /// </summary>
    public long PageCount => (long)Math.Ceiling((double)TotalItemCount / PageSize);

    /// <summary>
    /// Indicates whether a page exists before the current page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Indicates whether a page exists after the current page.
    /// </summary>
    public bool HasNextPage => PageNumber < PageCount;

    /// <summary>
    /// Indicates whether the current page is the first page.
    /// </summary>
    public bool IsFirstPage => PageNumber == 1;

    /// <summary>
    /// Indicates whether the current page is the last page.
    /// </summary>
    public bool IsLastPage => PageNumber >= PageCount;

    /// <summary>
    /// 1-based index of the first item on the current page.
    /// </summary>
    public long FirstItemOnPage => (PageNumber - 1) * PageSize + 1;

    /// <summary>
    /// 1-based index of the last item on the current page.
    /// </summary>
    public long LastItemOnPage => Math.Min(FirstItemOnPage + PageSize - 1, TotalItemCount);
}
