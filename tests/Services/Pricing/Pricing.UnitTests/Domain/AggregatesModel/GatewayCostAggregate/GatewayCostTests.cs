using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.UnitTests.Domain.AggregatesModel.GatewayCostAggregate;

public class GatewayCostTests
{
    private static Dictionary<int, decimal> OneRate() => new() { [1] = 3.0m };

    [Fact]
    public void Create_ShouldSucceed_WithValidRates()
    {
        var gatewayCost = GatewayCost.Create(GatewayProvider.Of("Simulated"), 0.39m, 2.5m, OneRate());

        Assert.Equal(0.39m, gatewayCost.FlatFeePerTransaction);
        Assert.Equal(2.5m, gatewayCost.AvistaRatePercent);
        Assert.Single(gatewayCost.InstallmentRates);
    }

    [Fact]
    public void Create_ShouldThrow_WhenFlatFeeIsNegative()
    {
        Assert.Throws<GatewayCostBadRequestException>(
            () => GatewayCost.Create(GatewayProvider.Of("Simulated"), -1m, 2.5m, OneRate()));
    }

    [Fact]
    public void Create_ShouldThrow_WhenAvistaRateIsNegative()
    {
        Assert.Throws<GatewayCostBadRequestException>(
            () => GatewayCost.Create(GatewayProvider.Of("Simulated"), 0m, -1m, OneRate()));
    }

    [Fact]
    public void Create_ShouldThrow_WhenInstallmentRatesIsEmpty()
    {
        Assert.Throws<GatewayCostBadRequestException>(
            () => GatewayCost.Create(GatewayProvider.Of("Simulated"), 0m, 2.5m, new Dictionary<int, decimal>()));
    }

    [Fact]
    public void Create_ShouldThrow_WhenAnInstallmentCountIsNotPositive()
    {
        Assert.Throws<GatewayCostBadRequestException>(
            () => GatewayCost.Create(
                GatewayProvider.Of("Simulated"), 0m, 2.5m, new Dictionary<int, decimal> { [0] = 1m }));
    }

    [Fact]
    public void Create_ShouldThrow_WhenInstallmentRatesLacksCountOne()
    {
        Assert.Throws<GatewayCostBadRequestException>(
            () => GatewayCost.Create(
                GatewayProvider.Of("Simulated"), 0m, 2.5m, new Dictionary<int, decimal> { [2] = 3m }));
    }

    [Fact]
    public void Update_ShouldChangeRates()
    {
        var gatewayCost = GatewayCost.Create(GatewayProvider.Of("Simulated"), 0.39m, 2.5m, OneRate());

        gatewayCost.Update(0.5m, 3m, new Dictionary<int, decimal> { [1] = 4m, [2] = 5m });

        Assert.Equal(0.5m, gatewayCost.FlatFeePerTransaction);
        Assert.Equal(2, gatewayCost.InstallmentRates.Count);
    }

    [Fact]
    public void GatewayProvider_Of_ShouldThrow_WhenEmpty()
    {
        Assert.Throws<GatewayCostBadRequestException>(() => GatewayProvider.Of(""));
    }
}
