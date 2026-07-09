using Catalog.Function.Modules.Categories.Features.CreateCategory;

namespace Catalog.UnitTests.Application.Commands;

public class CreateCategoryTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ICategoryRepository> _categoryRepository;
    private readonly CreateCategoryCommandValidator _validator;
    private readonly CreateCategoryHandler _handler;

    public CreateCategoryTests()
    {
        _autoMocker = new AutoMocker();
        _categoryRepository = _autoMocker.GetMock<ICategoryRepository>();
        _validator = new CreateCategoryCommandValidator();
        _handler = _autoMocker.CreateInstance<CreateCategoryHandler>();
    }

    [Fact]
    public async Task Handle_ShouldPersistRootCategory_WhenNoParentGiven()
    {
        var command = new CreateCategoryCommand("Languages", null);

        Category? saved = null;
        _categoryRepository
            .Setup(repo => repo.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.NotNull(saved);
        Assert.True(saved!.IsRoot);
        Assert.Empty(saved.Path);
    }

    [Fact]
    public async Task Handle_ShouldComputePath_FromParentsPath()
    {
        var parent = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(parent.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var command = new CreateCategoryCommand("C#", parent.Id.Value);

        Category? saved = null;
        _categoryRepository
            .Setup(repo => repo.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        await _handler.Handle(command, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal([parent.Id], saved!.Path);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenParentDoesNotExist()
    {
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var command = new CreateCategoryCommand("C#", Guid.NewGuid());

        await Assert.ThrowsAsync<CategoryParentBadRequestException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenNameIsEmpty()
    {
        var result = _validator.TestValidate(new CreateCategoryCommand("", null));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
