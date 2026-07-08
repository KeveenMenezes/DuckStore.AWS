namespace Catalog.Function.Modules.Categories.Features.UpdateCategory;

public class UpdateCategoryHandler(ICategoryRepository categoryRepository)
    : ICommandHandler<UpdateCategoryCommand, UpdateCategoryResult>
{
    public async Task<UpdateCategoryResult> Handle(
        UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new CategoryIdBadRequestException(command.CategoryId);

        var renamed = category.Name != command.Name;
        if (renamed)
            category.Rename(command.Name);

        var moved = command.ParentId != category.ParentId?.Value;
        if (moved)
            await MoveAsync(category, command.ParentId, cancellationToken);

        await categoryRepository.UpdateAsync(category, cancellationToken);

        return new UpdateCategoryResult(category.Id.Value, renamed, moved);
    }

    private async Task MoveAsync(Category category, Guid? newParentId, CancellationToken cancellationToken)
    {
        if (newParentId == category.Id.Value)
            throw new CategoryParentBadRequestException(newParentId.Value, "a category cannot be its own parent");

        List<CategoryId> newParentPath = [];
        CategoryId? newParent = null;

        if (newParentId is { } rawNewParentId)
        {
            var parent = await categoryRepository.GetByIdAsync(rawNewParentId, cancellationToken)
                ?? throw new CategoryParentBadRequestException(rawNewParentId, "parent category does not exist");

            newParent = parent.Id;
            newParentPath = parent.Path;
        }

        var descendants = await categoryRepository.ListDescendantsAsync(category.Id.Value, cancellationToken);

        if (newParentId is { } candidateId && descendants.Any(d => d.Id.Value == candidateId))
            throw new CategoryParentBadRequestException(
                candidateId, "cannot move a category under one of its own descendants");

        var oldPath = category.Path;
        List<CategoryId> newPath = newParent is null ? [] : [.. newParentPath, newParent];

        category.Move(newParent, newPath);

        // Cascade: every descendant's Path had the old ancestor prefix (oldPath + category.Id);
        // rewrite that prefix to the new one (newPath + category.Id) and persist.
        List<CategoryId> oldPrefix = [.. oldPath, category.Id];
        List<CategoryId> newPrefix = [.. newPath, category.Id];

        foreach (var descendant in descendants)
        {
            var suffix = descendant.Path.Skip(oldPrefix.Count).ToList();
            List<CategoryId> descendantPath = [.. newPrefix, .. suffix];

            descendant.Move(descendant.ParentId, descendantPath);
            await categoryRepository.UpdateAsync(descendant, cancellationToken);
        }
    }
}
