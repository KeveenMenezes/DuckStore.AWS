namespace Catalog.Function.Modules.Categories.Features.CreateCategory;

public record CreateCategoryCommand(string Name, Guid? ParentId) : ICommand<CreateCategoryResult>;

public record CreateCategoryResult(Guid Id);

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required");
    }
}
