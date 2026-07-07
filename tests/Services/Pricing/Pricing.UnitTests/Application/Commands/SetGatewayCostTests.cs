using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Modules.GatewayCosts.Features.SetGatewayCost;

namespace Pricing.UnitTests.Application.Commands;

public class SetGatewayCostTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository;
    private readonly SetGatewayCostCommandValidator _validator;
    private readonly SetGatewayCostHandler _handler;

    public SetGatewayCostTests()
    {
        _autoMocker = new AutoMocker();
        _gatewayCostRepository = _autoMocker.GetMock<IGatewayCostRepository>();
        _validator = new SetGatewayCostCommandValidator();
        _handler = _autoMocker.CreateInstance<SetGatewayCostHandler>();
    }

    private static SetGatewayCostCommand ValidCommand() => new(
        "Simulated", 0.39m, 2.5m, new Dictionary<int, decimal> { [1] = 3.0m, [12] = 14.0m });

    [Fact]
    public async Task Handle_ShouldCreateGatewayCost_WhenNoneExistsYet()
    {
        var command = ValidCommand();
        _gatewayCostRepository
            .Setup(repo => repo.GetByProviderAsync(command.Provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GatewayCost?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Simulated", result.Provider);
        Assert.Equal(2, result.InstallmentRates.Count);
        _gatewayCostRepository.Verify(
            repo => repo.PutAsync(It.IsAny<GatewayCost>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldUpdateExistingGatewayCost_WhenOneAlreadyExists()
    {
        var command = ValidCommand();
        var existing = GatewayCost.Create(
            GatewayProvider.Of("Simulated"), 0.10m, 1.0m, new Dictionary<int, decimal> { [1] = 1m });
        _gatewayCostRepository
            .Setup(repo => repo.GetByProviderAsync(command.Provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(0.39m, result.FlatFeePerTransaction);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenProviderIsEmpty()
    {
        var command = ValidCommand() with { Provider = "" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Provider);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenInstallmentRatesIsEmpty()
    {
        var command = ValidCommand() with { InstallmentRates = [] };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.InstallmentRates);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenFlatFeeIsNegative()
    {
        var command = ValidCommand() with { FlatFeePerTransaction = -1m };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FlatFeePerTransaction);
    }
}
