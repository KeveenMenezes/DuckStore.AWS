namespace CatalogView.Function.Modules.Products.Queries.GetProduct;

public sealed class GetProductHandler(IProductSearchIndex index)
{
    public async Task<GetProductResponse?> HandleAsync(
        GetProductRequest request, CancellationToken cancellationToken = default)
    {
        var document = await index.GetAsync(request.Id, cancellationToken);

        return document is null
            ? null
            : new GetProductResponse
            {
                Id = document.Id,
                Name = document.Name,
                Description = document.Description,
                ImageUrl = document.ImageUrl,
                OriginalPrice = document.OriginalPrice,
                Stock = document.Stock,
                CategoryIds = document.CategoryIds,
                AverageRating = document.AverageRating,
                RatingCount = document.RatingCount,
                Price = document.Price,
                CashPrice = document.CashPrice,
                MaxInstallmentsWithoutInterest = document.MaxInstallmentsWithoutInterest,
                MaxInstallmentValue = document.MaxInstallmentValue
            };
    }
}
