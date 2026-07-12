namespace Catalog.Function.Modules.Categories.Domain.Entities;

public class Category : Aggregate<CategoryId>
{
    public string Name { get; private set; } = default!;
    public CategoryId? ParentId { get; private set; } = default!;
    public List<CategoryId> Path { get; private set; } = default!;
    public bool IsRoot => ParentId is null;

    public static Category Create(
        CategoryId id, string name, CategoryId? parentId = null, List<CategoryId>? parentPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var category = new Category
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            Path = parentId is null ? [] : [.. parentPath ?? [], parentId]
        };

        return category;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    // Cycle detection (new parent cannot be the category itself or one of its own descendants) is
    // orchestration done by the caller, which needs repository access to resolve descendants —
    // the aggregate only applies the already-validated result. newPath is the new parent's own
    // Path with newParentId appended (empty for a root move).
    public void Move(CategoryId? newParentId, List<CategoryId> newPath)
    {
        ParentId = newParentId;
        Path = newPath;
    }

    internal static Category Load(
        Guid id, string name, Guid? parentId, List<CategoryId> path) =>
        new()
        {
            Id = CategoryId.Of(id),
            Name = name,
            ParentId = parentId.HasValue ? CategoryId.Of(parentId.Value) : null,
            Path = path
        };
}
