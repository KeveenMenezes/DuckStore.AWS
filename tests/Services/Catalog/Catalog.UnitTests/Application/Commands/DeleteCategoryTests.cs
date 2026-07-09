using Catalog.Function.Modules.Categories.Features.DeleteCategory;

namespace Catalog.UnitTests.Application.Commands;

public class DeleteCategoryTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ICategoryRepository> _categoryRepository;
    private readonly Mock<IProductRepository> _productRepository;
    private readonly DeleteCategoryCommandValidator _validator;
    private readonly DeleteCategoryHandler _handler;

    public DeleteCategoryTests()
    {
        _autoMocker = new AutoMocker();
        _categoryRepository = _autoMocker.GetMock<ICategoryRepository>();
        _productRepository = _autoMocker.GetMock<IProductRepository>();
        _validator = new DeleteCategoryCommandValidator();
        _handler = _autoMocker.CreateInstance<DeleteCategoryHandler>();

        _productRepository
            .Setup(repo => repo.AnyReferencingCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task Handle_ShouldDeleteCategory_WhenNoChildrenOrProductsReferenceIt()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Specials");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _categoryRepository
            .Setup(repo => repo.ListChildrenAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.Handle(new DeleteCategoryCommand(category.Id.Value), CancellationToken.None);

        Assert.True(result.Deleted);
        _categoryRepository.Verify(
            repo => repo.DeleteAsync(category.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenCategoryHasChildren()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Languages");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _categoryRepository
            .Setup(repo => repo.ListChildrenAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Category.Create(CategoryId.Of(Guid.NewGuid()), "C#", category.Id, [])]);

        await Assert.ThrowsAsync<CategoryHasChildrenBadRequestException>(
            () => _handler.Handle(new DeleteCategoryCommand(category.Id.Value), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenAProductStillReferencesTheCategory()
    {
        var category = Category.Create(CategoryId.Of(Guid.NewGuid()), "Specials");
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _categoryRepository
            .Setup(repo => repo.ListChildrenAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _productRepository
            .Setup(repo => repo.AnyReferencingCategoryAsync(category.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<CategoryHasProductsBadRequestException>(
            () => _handler.Handle(new DeleteCategoryCommand(category.Id.Value), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenCategoryDoesNotExist()
    {
        _categoryRepository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        await Assert.ThrowsAsync<CategoryIdBadRequestException>(
            () => _handler.Handle(new DeleteCategoryCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCategoryIdIsEmpty()
    {
        var result = _validator.TestValidate(new DeleteCategoryCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
    }
}
