namespace Catalog.Function.Modules.Categories.Features.DeleteCategory;

public record DeleteCategoryCommand(Guid CategoryId) : ICommand<DeleteCategoryResult>;

public record DeleteCategoryResult(Guid CategoryId, bool Deleted);

public class DeleteCategoryCommandValidator : AbstractValidator<DeleteCategoryCommand>
{
    public DeleteCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("CategoryId is required");
    }
}
