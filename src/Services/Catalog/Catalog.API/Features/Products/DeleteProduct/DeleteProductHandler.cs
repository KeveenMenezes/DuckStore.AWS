namespace Catalog.API.Features.Products.DeleteProduct;

public record DeleteProductCommand(Guid Id)
    : ICommand<DeleteProductResult>;

public record DeleteProductResult(bool IsSuccess);

public class DeleteProductCommandValitor : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValitor()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");
    }
}

public class DeleteProductHandler
    (IProductRepository productRepository)
    : ICommandHandler<DeleteProductCommand, DeleteProductResult>
{
    public async Task<DeleteProductResult> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        await productRepository.DeleteAsync(request.Id, cancellationToken);

        return new DeleteProductResult(true);
    }
}



