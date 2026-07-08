namespace Catalog.Function.Modules.Categories.Features.DeleteCategory;

public class DeleteCategoryHandler(ICategoryRepository categoryRepository, IProductRepository productRepository)
    : ICommandHandler<DeleteCategoryCommand, DeleteCategoryResult>
{
    public async Task<DeleteCategoryResult> Handle(
        DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        _ = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new CategoryIdBadRequestException(command.CategoryId);

        var children = await categoryRepository.ListChildrenAsync(command.CategoryId, cancellationToken);
        if (children.Count > 0)
            throw new CategoryHasChildrenBadRequestException(command.CategoryId);

        if (await productRepository.AnyReferencingCategoryAsync(command.CategoryId, cancellationToken))
            throw new CategoryHasProductsBadRequestException(command.CategoryId);

        await categoryRepository.DeleteAsync(command.CategoryId, cancellationToken);

        return new DeleteCategoryResult(command.CategoryId, true);
    }
}
