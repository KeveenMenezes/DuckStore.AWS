namespace Catalog.UnitTests.Products;

public class DeleteProductHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IDocumentSession> _sessionMock;
    private readonly DeleteProductCommandValitor _validator;
    private readonly DeleteProductHandler _handler;

    public DeleteProductHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _sessionMock = _autoMocker.GetMock<IDocumentSession>();
        _validator = new DeleteProductCommandValitor();
        _handler = new DeleteProductHandler(_sessionMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldDeleteProductSuccessfully()
    {
        // Arrange
        var command = new DeleteProductCommand(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);

        _sessionMock.Verify(session =>
            session.Delete<Product>(command.Id), Times.Once);

        _sessionMock.Verify(session =>
            session.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Handle_ShouldNotError_WhenCommandHasValidId()
    {
        // Arrange
        var command = new DeleteProductCommand(Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCommandHasInvalidId()
    {
        // Arrange
        var command = new DeleteProductCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Id is required.");
    }
}
