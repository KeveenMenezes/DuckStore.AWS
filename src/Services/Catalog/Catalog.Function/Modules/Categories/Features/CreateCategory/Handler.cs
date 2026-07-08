namespace Catalog.Function.Modules.Categories.Features.CreateCategory;

public class CreateCategoryHandler(ICategoryRepository categoryRepository)
    : ICommandHandler<CreateCategoryCommand, CreateCategoryResult>
{
    public async Task<CreateCategoryResult> Handle(
        CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        CategoryId? parentId = null;
        List<CategoryId>? parentPath = null;

        if (command.ParentId is { } rawParentId)
        {
            var parent = await categoryRepository.GetByIdAsync(rawParentId, cancellationToken)
                ?? throw new CategoryParentBadRequestException(rawParentId, "parent category does not exist");

            parentId = parent.Id;
            parentPath = parent.Path;
        }

        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), command.Name, parentId, parentPath);

        await categoryRepository.AddAsync(category, cancellationToken);

        return new CreateCategoryResult(category.Id.Value);
    }
}
