using Catalog.Function.Modules.Categories.Features.UpdateCategory;

namespace Catalog.UnitTests.Application.Commands;

public class UpdateCategoryTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ICategoryRepository> _categoryRepository;
    private readonly UpdateCategoryCommandValidator _validator;
    private readonly UpdateCategoryHandler _handler;

    public UpdateCategoryTests()
    {
        _autoMocker = new AutoMocker();
        _categoryRepository = _autoMocker.GetMock<ICategoryRepository>();
        _validator = new UpdateCategoryCommandValidator();
        _handler = _autoMocker.CreateInstance<UpdateCategoryHandler>();

        _categoryRepository
            .Setup(repo => repo.ListDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task Handle_ShouldRenameOnly_WhenParentIdUnchanged()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var command = new UpdateCategoryCommand(category.Id.Value, "Programming Languages", null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.Renamed);
        Assert.False(result.Moved);
        Assert.Equal("Programming Languages", category.Name);
        _categoryRepository.Verify(
            repo => repo.UpdateAsync(category, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldMoveAndComputeNewPath_WhenParentIdChanged()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "C#");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var newParent = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(newParent.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newParent);

        var command = new UpdateCategoryCommand(category.Id.Value, category.Name, newParent.Id.Value);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.Moved);
        Assert.Equal(newParent.Id, category.ParentId);
        Assert.Equal([newParent.Id], category.Path);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenMovingUnderItself()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "C#");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var command = new UpdateCategoryCommand(category.Id.Value, category.Name, category.Id.Value);

        await Assert.ThrowsAsync<CategoryParentBadRequestException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenMovingUnderOwnDescendant()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var descendant = Category.Create(CategoryId.Of(Guid.NewGuid()), "C#", category.Id, [category.Id]);
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(descendant.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(descendant);
        _categoryRepository
            .Setup(repo => repo.ListDescendantsAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync([descendant]);

        var command = new UpdateCategoryCommand(category.Id.Value, category.Name, descendant.Id.Value);

        await Assert.ThrowsAsync<CategoryParentBadRequestException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenCategoryDoesNotExist()
    {
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var command = new UpdateCategoryCommand(Guid.NewGuid(), "Languages", null);

        await Assert.ThrowsAsync<CategoryIdBadRequestException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenNameIsEmpty()
    {
        var result = _validator.TestValidate(new UpdateCategoryCommand(Guid.NewGuid(), "", null));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
