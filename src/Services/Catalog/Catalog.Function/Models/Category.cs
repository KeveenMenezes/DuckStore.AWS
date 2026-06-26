namespace Catalog.Function.Models;

public class Category : IdentifiableEntity<CategoryId, Guid>
{
    public string Name { get; private set; } = default!;
    public CategoryId? ParentId { get; private set; } = default!;
    public List<CategoryId> Path { get; private set; } = default!;
    public bool IsRoot => ParentId is null;

    public static Category Create(CategoryId id, string name, CategoryId? parentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var category = new Category
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            Path = []
        };

        return category;
    }

    // Reconstitui uma Category já persistida (sem revalidar regras de criação).
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
