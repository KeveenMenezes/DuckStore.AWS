namespace Shopping.Web.Server.Services;

public interface ICatalogService
{
    [Get("/products?pageNumber={pageNumber}&pageSize={pageSize}")]
    Task<GetProductsResponse> GetProducts(int? pageNumber = 1, int? pageSize = 10);

    [Get("/products/{id}")]
    Task<GetProductByIdResponse> GetProduct(Guid id);

    [Get("/products/category/{category}")]
    Task<GetProductByCategoryResponse> GetProductsByCategory(string category);
}