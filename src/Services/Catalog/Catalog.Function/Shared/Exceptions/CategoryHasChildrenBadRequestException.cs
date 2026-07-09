namespace Catalog.Function.Shared.Exceptions;

public class CategoryHasChildrenBadRequestException(Guid categoryId)
    : BadRequestException(
        "CategoryId",
        categoryId,
        "category has subcategories and cannot be deleted");
