namespace Catalog.Function.Modules.Categories.Features.UpdateCategory;

public record UpdateCategoryCommand(Guid CategoryId, string Name, Guid? ParentId) : ICommand<UpdateCategoryResult>;

public record UpdateCategoryResult(Guid CategoryId, bool Renamed, bool Moved);

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("CategoryId is required");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required");
    }
}
