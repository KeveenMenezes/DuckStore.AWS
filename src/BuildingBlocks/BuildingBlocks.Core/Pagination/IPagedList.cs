namespace BuildingBlocks.Core.Pagination;

public class PagedList(
    long pageNumber,
    long pageSize,
    long totalItemCount
)
{
    /// <summary>
    /// Número da página atual (começando de 1).
    /// </summary>
    public long PageNumber { get; } = pageNumber;

    /// <summary>
    /// Número de itens por página.
    /// </summary>
    public long PageSize { get; } = pageSize;

    /// <summary>
    /// Número total de itens na consulta.
    /// </summary>
    public long TotalItemCount { get; } = totalItemCount;

    /// <summary>
    /// Número total de páginas disponíveis.
    /// </summary>
    public long PageCount => (long)Math.Ceiling((double)TotalItemCount / PageSize);

    /// <summary>
    /// Indica se existe uma página anterior à página atual.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Indica se existe uma próxima página após a página atual.
    /// </summary>
    public bool HasNextPage => PageNumber < PageCount;

    /// <summary>
    /// Indica se a página atual é a primeira página.
    /// </summary>
    public bool IsFirstPage => PageNumber == 1;

    /// <summary>
    /// Indica se a página atual é a última página.
    /// </summary>
    public bool IsLastPage => PageNumber >= PageCount;

    /// <summary>
    /// Índice baseado em 1 do primeiro item na página atual.
    /// </summary>
    public long FirstItemOnPage => (PageNumber - 1) * PageSize + 1;

    /// <summary>
    /// Índice baseado em 1 do último item na página atual.
    /// </summary>
    public long LastItemOnPage => Math.Min(FirstItemOnPage + PageSize - 1, TotalItemCount);
}
