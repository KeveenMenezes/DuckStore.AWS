namespace Catalog.UnitTests.Products;

public class CreateProductHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IDocumentSession> _sessionMock;
    private readonly CreateProductCommandValidator _validator;
    private readonly CreateProductCommandHandler _handler;

    public CreateProductHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _sessionMock = _autoMocker.GetMock<IDocumentSession>();
        _validator = new CreateProductCommandValidator();
        _handler = new CreateProductCommandHandler(_sessionMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldCreateProductSuccessfully()
    {
        // Arrange
        var command = new CreateProductCommand(
            "Product Name",
            "Product Description",
            "http://image.url",
            100.0m,
            ["Category1", "Category2"]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);

        _sessionMock.Verify(session =>
            session.Store(It.IsAny<Product>()), Times.Once);

        _sessionMock.Verify(session =>
            session.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Handle_ShouldNotError_WhenCommandHasValidValues()
    {
        // Arrange
        var command = new CreateProductCommand(
            "Product Name",
            "Product Description",
            "http://image.url",
            100.0m,
            ["Category1", "Category2"]);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
        result.ShouldNotHaveValidationErrorFor(x => x.Price);
        result.ShouldNotHaveValidationErrorFor(x => x.Categories);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCommandHasInvalidValues()
    {
        // Arrange
        var command = new CreateProductCommand(
            "",
            "",
            "",
            -10.0m,
            []);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name is required");

        result.ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Name is required");

        result.ShouldHaveValidationErrorFor(x => x.ImageUrl)
            .WithErrorMessage("Image is required");

        result.ShouldHaveValidationErrorFor(x => x.Price)
            .WithErrorMessage("Price must be greater than 0");

        result.ShouldHaveValidationErrorFor(x => x.Categories)
            .WithErrorMessage("Categories is required");
    }
}
