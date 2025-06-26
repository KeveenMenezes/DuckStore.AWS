namespace Catalog.API.Features.Products.UpdateProduct;

public record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    string ImageUrl,
    decimal Price,
    int Stock,
    List<Guid> CategoryIds)
    : ICommand<UpdateProductResult>;

public record UpdateProductResult(Guid Id);

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .Length(3, 100).WithMessage("Name must be between 3 and 100 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than 0.");
    }
}

public class UpdateProductCommandHandler
    (IDocumentSession session)
    : ICommandHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<UpdateProductResult> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await session.LoadAsync<Product>(
            command.Id, cancellationToken) ??
                throw new ProductNotFoundException(command.Id);

        product.Update(
            command.Name,
            command.Description,
            command.ImageUrl,
            command.Price,
            command.Stock,
            CategoryId.Of(command.CategoryIds));

        session.Update(product);

        return new UpdateProductResult(product.Id.Value);
    }
}
