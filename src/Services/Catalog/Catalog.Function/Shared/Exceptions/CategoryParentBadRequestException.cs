namespace Catalog.Function.Shared.Exceptions;

public class CategoryParentBadRequestException(Guid parentId, string message)
    : BadRequestException(
        "ParentId",
        parentId,
        message);
