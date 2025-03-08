namespace Catalog.API.Products.CreateProduct;

public record CreateProductCommand(
    string Name,
    string Description,
    string ImageUrl,
    decimal Price,
    List<string> Categories)
    : ICommand<CreateProductResult>;

public record CreateProductResult(Guid Id);

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        RuleFor(x => x.Description).NotEmpty().WithMessage("Name is required");
        RuleFor(x => x.ImageUrl).NotEmpty().WithMessage("Image is required");
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be greater than 0");
        RuleFor(x => x.Categories).NotEmpty().WithMessage("Categories is required");
    }
}

internal class CreateProductCommandHandler
    (IDocumentSession session)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = new Product(
            Guid.NewGuid(),
            command.Name,
            command.Description,
            command.ImageUrl,
            command.Price,
            command.Categories
        );

        //save to database
        session.Store(product);
        await session.SaveChangesAsync(cancellationToken);

        //return result
        return new CreateProductResult(product.Id);
    }
}
