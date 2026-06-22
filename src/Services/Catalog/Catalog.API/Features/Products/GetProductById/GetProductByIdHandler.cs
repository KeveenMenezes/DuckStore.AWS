namespace Catalog.API.Features.Products.GetProductById;

public record GetProductByIdQuery(Guid Id) :
    IQuery<GetProductByIdResult>;

public record GetProductByIdResult(Product Product);

public class GetProductByIdQueryHandler(
    IProductRepository productRepository)
    : IQueryHandler<GetProductByIdQuery, GetProductByIdResult>
{
    public async Task<GetProductByIdResult> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(query.Id, cancellationToken) ??
            throw new ProductNotFoundException(query.Id);

        return new GetProductByIdResult(product);
    }
}
