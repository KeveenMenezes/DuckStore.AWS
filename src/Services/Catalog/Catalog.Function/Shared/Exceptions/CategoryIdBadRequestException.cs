namespace Catalog.Function.Shared.Exceptions;

public class CategoryIdBadRequestException(Guid categoryId)
    : BadRequestException(
        "CategoryId",
        categoryId);
