using System.Net.Http.Headers;
using System.Text.Json;

namespace Managment.Web.Blazor.Services;

/// <summary>
/// Typed wrapper over the product CRUD GraphQL operations. Operation shapes mirror
/// graphql/schema.graphql in the React SPA (the single schema source of truth).
/// Reads come from CatalogView ("catalogview-products") while writes go to Catalog's
/// "products" table — synced via CDC, so a write may take a few seconds to show up.
/// </summary>
public sealed class ProductAdminService(GraphQLClient gql)
{
    private const string GetProductsQuery =
        """
        query GetProducts($query: String, $pageSize: Int, $nextToken: String) {
          products(query: $query, pageSize: $pageSize, nextToken: $nextToken) {
            items {
              id
              name
              description
              images { imageId isMain order }
              stock
              categoryIds
              averageRating
              ratingCount
              price
              originalPrice
              cashPrice
              maxInstallmentsWithoutInterest
              maxInstallmentValue
            }
            nextToken
          }
        }
        """;

    private const string GetProductQuery =
        """
        query GetProduct($id: ID!) {
          product(id: $id) {
            id
            name
            description
            images { imageId isMain order }
            stock
            categoryIds
            averageRating
            ratingCount
            price
            originalPrice
            cashPrice
            maxInstallmentsWithoutInterest
            maxInstallmentValue
          }
        }
        """;

    private const string GetCategoriesQuery =
        """
        query GetCategories($pageSize: Int, $nextToken: String) {
          categories(pageSize: $pageSize, nextToken: $nextToken) {
            items {
              id
              name
              parentId
            }
            nextToken
          }
        }
        """;

    private const string CreateProductImageUploadMutation =
        """
        mutation CreateProductImageUpload($input: CreateProductImageUploadInput!) {
          createProductImageUpload(input: $input) {
            imageId
            url
            fields
          }
        }
        """;

    private const string NominalPriceForQuery =
        """
        query NominalPriceFor($productId: ID!) {
          nominalPriceFor(productId: $productId) {
            productId
            nominalPrice
            cost
          }
        }
        """;

    private const string SetNominalPriceMutation =
        """
        mutation SetNominalPrice($productId: ID!, $price: Float!, $cost: Float!) {
          setNominalPrice(productId: $productId, price: $price, cost: $cost) {
            productId
            nominalPrice
            cost
          }
        }
        """;

    private const string CreateProductWithPriceMutation =
        """
        mutation CreateProductWithPrice($input: CreateProductWithPriceInput!) {
          createProductWithPrice(input: $input) {
            id
          }
        }
        """;

    private const string UpdateProductMutation =
        """
        mutation UpdateProduct($input: UpdateProductInput!) {
          updateProduct(input: $input) {
            id
          }
        }
        """;

    private const string DeleteProductMutation =
        """
        mutation DeleteProduct($id: ID!) {
          deleteProduct(id: $id) {
            isSuccess
          }
        }
        """;

    public async Task<ProductPage> GetProductsAsync(string? search = null, int pageSize = 12, string? nextToken = null)
    {
        var data = await gql.SendAsync<ProductsData>(
            GetProductsQuery,
            new { query = string.IsNullOrWhiteSpace(search) ? null : search, pageSize, nextToken });
        return data.Products;
    }

    public async Task<Product?> GetProductAsync(string id)
    {
        var data = await gql.SendAsync<ProductData>(GetProductQuery, new { id });
        return data.Product;
    }

    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        var categories = new List<Category>();
        string? nextToken = null;
        do
        {
            var data = await gql.SendAsync<CategoriesData>(GetCategoriesQuery, new { pageSize = 100, nextToken });
            categories.AddRange(data.Categories.Items);
            nextToken = data.Categories.NextToken;
        } while (nextToken is not null);
        return categories;
    }

    // Only used by the campaign product multi-select, which needs the full catalog to render
    // checkboxes for — ProductList itself stays paginated.
    public async Task<List<Product>> GetAllProductsAsync()
    {
        var products = new List<Product>();
        string? nextToken = null;
        do
        {
            var page = await GetProductsAsync(pageSize: 100, nextToken: nextToken);
            products.AddRange(page.Items);
            nextToken = page.NextToken;
        } while (nextToken is not null);
        return products;
    }

    // Plain client for the presigned POSTs — S3 rejects requests carrying the AppSync
    // Authorization header, so the GraphQL client's HttpClient must not be reused here.
    private static readonly HttpClient S3Http = new();

    public async Task<PriceInfo?> GetNominalPriceAsync(string productId)
    {
        var data = await gql.SendAsync<NominalPriceData>(NominalPriceForQuery, new { productId });
        return data.NominalPriceFor;
    }

    // No batch nominalPriceFor query exists, so a page of products means N parallel calls
    // (bounded by pageSize) — same cost class as the single-product fetch ProductEdit does.
    public async Task<Dictionary<string, double?>> GetNominalPricesAsync(IEnumerable<string> productIds)
    {
        var ids = productIds.ToList();
        var results = await Task.WhenAll(ids.Select(GetNominalPriceAsync));
        return ids.Zip(results, (id, price) => (id, price))
            .ToDictionary(x => x.id, x => x.price?.NominalPrice);
    }

    public async Task<List<PresignedImageUpload>> CreatePresignedUploadsAsync(List<string> contentTypes)
    {
        var data = await gql.SendAsync<CreateProductImageUploadData>(
            CreateProductImageUploadMutation,
            new { input = new { contentTypes } });
        return data.CreateProductImageUpload;
    }

    // Presigned POST upload (ADR-0034): every policy field verbatim first, the file last
    // in the "file" field — S3 ignores anything after it.
    public static async Task UploadToS3Async(
        PresignedImageUpload presigned, Stream content, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(presigned.Fields) ?? [];
        foreach (var (key, value) in fields)
            form.Add(new StringContent(value), key);

        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "upload");

        using var response = await S3Http.PostAsync(presigned.Url, form, ct);
        response.EnsureSuccessStatusCode();
    }

    // Single mutation backed by a Step Functions saga (ADR-0032) — the Catalog product
    // and the Pricing nominal price are written in one execution, with a compensating
    // delete if the price write fails. No duplicate product on retry.
    public async Task<string> CreateProductAsync(ProductFormModel model)
    {
        var input = new
        {
            name = model.Name,
            description = model.Description,
            images = ToImagesInput(model),
            stock = model.Stock,
            categoryIds = model.CategoryIds,
            price = model.Price,
            cost = model.Cost,
        };
        var data = await gql.SendAsync<CreateProductWithPriceData>(CreateProductWithPriceMutation, new { input });
        return data.CreateProductWithPrice.Id;
    }

    // Price/cost live in Pricing, not Catalog (ADR-0026), so updating is still two
    // mutations. Not atomic, but both are idempotent — a retry after a partial
    // failure converges.
    public async Task<string> UpdateProductAsync(string id, ProductFormModel model)
    {
        var input = new
        {
            id,
            name = model.Name,
            description = model.Description,
            images = ToImagesInput(model),
            stock = model.Stock,
            categoryIds = model.CategoryIds,
        };
        var data = await gql.SendAsync<UpdateProductData>(UpdateProductMutation, new { input });
        await SetNominalPriceAsync(id, model);
        return data.UpdateProduct.Id;
    }

    private Task SetNominalPriceAsync(string productId, ProductFormModel model) =>
        gql.SendAsync<SetNominalPriceData>(
            SetNominalPriceMutation,
            new { productId, price = model.Price, cost = model.Cost });

    // Order is the list position — the form reorders by moving items in the list.
    private static object ToImagesInput(ProductFormModel model) =>
        model.Images.Select((image, index) => new
        {
            imageId = image.ImageId,
            isMain = image.IsMain,
            order = index,
        }).ToList();

    public async Task<bool> DeleteProductAsync(string id)
    {
        var data = await gql.SendAsync<DeleteProductData>(DeleteProductMutation, new { id });
        return data.DeleteProduct.IsSuccess;
    }

    private sealed record ProductsData(ProductPage Products);

    private sealed record ProductData(Product? Product);

    private sealed record CategoriesData(CategoryPage Categories);

    private sealed record NominalPriceData(PriceInfo? NominalPriceFor);

    private sealed record CreateProductImageUploadData(List<PresignedImageUpload> CreateProductImageUpload);

    private sealed record SetNominalPriceData(PriceInfo SetNominalPrice);

    private sealed record CreateProductWithPriceData(CreateProductResult CreateProductWithPrice);

    private sealed record UpdateProductData(UpdateProductResult UpdateProduct);

    private sealed record DeleteProductData(DeleteProductResult DeleteProduct);
}
