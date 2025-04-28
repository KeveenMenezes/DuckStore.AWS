namespace Catalog.UnitTests.Products;

public class UpdateProductCommandHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IDocumentSession> _sessionMock;
    private readonly UpdateProductCommandValidator _validator;
    private readonly UpdateProductCommandHandler _handler;

    public UpdateProductCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _sessionMock = _autoMocker.GetMock<IDocumentSession>();
        _validator = new UpdateProductCommandValidator();
        _handler = new UpdateProductCommandHandler(_sessionMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldUpdateProductSuccessfully()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var product = new Product(
            productId,
            "OldName",
            "OldDescription",
            "http://oldimage.url",
            50.0m,
            ["OldCategory"]);

        _sessionMock.Setup(session => session.LoadAsync<Product>(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var command = new UpdateProductCommand(
            productId,
            "NewName",
            "NewDescription",
            "http://newimage.url",
            100.0m,
            ["NewCategory"]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(productId, result.Id);

        _sessionMock.Verify(session => session.Update(It.IsAny<Product>()), Times.Once);
        _sessionMock.Verify(session => session.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("NewName", product.Name);
        Assert.Equal("NewDescription", product.Description);
        Assert.Equal("http://newimage.url", product.ImageUrl);
        Assert.Equal(100.0m, product.Price);
        Assert.Contains("NewCategory", product.Categories);
    }

    [Fact]
    public async Task Handle_ShouldThrowException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = Guid.NewGuid();

        _sessionMock.Setup(session => session.LoadAsync<Product>(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product)null);

        var command = new UpdateProductCommand(
            productId,
            "NewName",
            "NewDescription",
            "http://newimage.url",
            100.0m,
            ["NewCategory"]);

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
            ["ValidCategory"]);

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
