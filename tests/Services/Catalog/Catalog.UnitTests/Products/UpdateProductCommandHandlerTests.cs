namespace Catalog.UnitTests.Products;

public class UpdateProductCommandHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly UpdateProductCommandValidator _validator;
    private readonly UpdateProductCommandHandler _handler;

    public UpdateProductCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _productRepositoryMock = _autoMocker.GetMock<IProductRepository>();
        _validator = new UpdateProductCommandValidator();
        _handler = new UpdateProductCommandHandler(_productRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldUpdateProductSuccessfully()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = Product.Create(
            productId, "OldName", "OldDescription", "http://oldimage.url", 50.0m, 1, [CategoryId.Of(Guid.NewGuid())]);

        _productRepositoryMock
            .Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var newCategoryId = Guid.NewGuid();
        var command = new UpdateProductCommand(
            productId,
            "NewName",
            "NewDescription",
            "http://newimage.url",
            100.0m,
            5,
            [newCategoryId]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(productId, result.Id);

        _productRepositoryMock.Verify(repo =>
            repo.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("NewName", product.Name);
        Assert.Equal("NewDescription", product.Description);
        Assert.Equal("http://newimage.url", product.ImageUrl);
        Assert.Equal(100.0m, product.Price);
        Assert.Contains(product.CategoryIds, c => c.Value == newCategoryId);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = Guid.NewGuid();

        _productRepositoryMock
            .Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var command = new UpdateProductCommand(
            productId,
            "NewName",
            "NewDescription",
            "http://newimage.url",
            100.0m,
            5,
            [Guid.NewGuid()]);

        // Act & Assert
        await Assert.ThrowsAsync<ProductNotFoundException>(() => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldNotHaveErrors_WhenCommandIsValid()
    {
        // Arrange
        var command = new UpdateProductCommand(
            Guid.NewGuid(),
            "ValidName",
            "ValidDescription",
            "http://validimage.url",
            100.0m,
            5,
            [Guid.NewGuid()]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
        result.ShouldNotHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Validator_ShouldHaveErrors_WhenCommandIsInvalid()
    {
        // Arrange
        var command = new UpdateProductCommand(
            Guid.Empty,
            "",
            "",
            "invalid-url",
            -10.0m,
            0,
            []);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Id is required.");

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name is required.");

        result.ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description is required.");

        result.ShouldHaveValidationErrorFor(x => x.Price)
            .WithErrorMessage("Price must be greater than 0.");
    }
}
