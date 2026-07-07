using Pricing.Function.Modules.Prices.Features.SetNominalPrice;

namespace Pricing.UnitTests.Application.Commands;

public class SetNominalPriceTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IPriceRepository> _priceRepository;
    private readonly SetNominalPriceCommandValidator _validator;
    private readonly SetNominalPriceHandler _handler;

    public SetNominalPriceTests()
    {
        _autoMocker = new AutoMocker();
        _priceRepository = _autoMocker.GetMock<IPriceRepository>();
        _validator = new SetNominalPriceCommandValidator();
        _handler = _autoMocker.CreateInstance<SetNominalPriceHandler>();
    }

    [Fact]
    public async Task Handle_ShouldCreatePrice_WhenNoneExistsYet()
    {
        var productId = Guid.NewGuid();
        var command = new SetNominalPriceCommand(productId, 29.90m, 15.00m);

        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Price?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(productId, result.ProductId);
        Assert.Equal(29.90m, result.NominalPrice);
        Assert.Equal(15.00m, result.Cost);
        _priceRepository.Verify(repo => repo.PutAsync(It.IsAny<Price>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldUpdateExistingPrice_WhenOneAlreadyExists()
    {
        var productId = Guid.NewGuid();
        var existing = Price.Create(ProductId.Of(productId), 29.90m, 15.00m);
        var command = new SetNominalPriceCommand(productId, 39.90m, 20.00m);

        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(39.90m, result.NominalPrice);
        Assert.Equal(20.00m, result.Cost);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenNominalPriceIsNotPositive()
    {
        var command = new SetNominalPriceCommand(Guid.NewGuid(), 0, 15.00m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NominalPrice);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCostIsNotPositive()
    {
        var command = new SetNominalPriceCommand(Guid.NewGuid(), 29.90m, 0);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Cost);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenProductIdIsEmpty()
    {
        var command = new SetNominalPriceCommand(Guid.Empty, 10, 5.00m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }
}
