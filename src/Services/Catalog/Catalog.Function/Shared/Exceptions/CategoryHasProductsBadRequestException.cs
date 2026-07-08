namespace Catalog.Function.Shared.Exceptions;

public class CategoryHasProductsBadRequestException(Guid categoryId)
    : BadRequestException(
        "CategoryId",
        categoryId,
        "category is still referenced by at least one product and cannot be deleted");
