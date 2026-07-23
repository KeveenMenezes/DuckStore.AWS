namespace Catalog.UnitTests.Domain;

public class CategoryTests
{
    [Fact]
    public void Create_ShouldHaveEmptyPath_WhenRoot()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");

        Assert.True(category.IsRoot);
        Assert.Empty(category.Path);
    }

    [Fact]
    public void Create_ShouldAppendParentToParentPath_WhenParentGiven()
    {
        var parentId = CategoryId.Of(Guid.NewGuid());
        var grandparentId = CategoryId.Of(Guid.NewGuid());

        var category = Category.Create(
            CategoryId.Of(Guid.NewGuid()), "C#", parentId, [grandparentId]);

        Assert.False(category.IsRoot);
        Assert.Equal([grandparentId, parentId], category.Path);
    }

    [Fact]
    public void Rename_ShouldUpdateName()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");

        category.Rename("Programming Languages");

        Assert.Equal("Programming Languages", category.Name);
    }

    [Fact]
    public void Rename_ShouldThrow_WhenNameIsEmpty()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");

        Assert.Throws<ArgumentException>(() => category.Rename(" "));
    }

    [Fact]
    public void Move_ShouldUpdateParentAndPath()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "C#");
        var newParentId = CategoryId.Of(Guid.NewGuid());

        category.Move(newParentId, [newParentId]);

        Assert.Equal(newParentId, category.ParentId);
        Assert.Equal([newParentId], category.Path);
        Assert.False(category.IsRoot);
    }

    [Fact]
    public void Move_ShouldClearParentAndPath_WhenMovedToRoot()
    {
        var parentId = CategoryId.Of(Guid.NewGuid());
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "C#", parentId, []);

        category.Move(null, []);

        Assert.True(category.IsRoot);
        Assert.Empty(category.Path);
    }
}
