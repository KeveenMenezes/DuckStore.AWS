using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.EventsIntegration.Publishers;
using Pricing.Function.Shared.Configuration;

namespace Pricing.UnitTests.Application.EventHandlers.Integration;

public class PricingHighlightsTests
{
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository = new();
    private readonly Mock<ICampaignRepository> _campaignRepository = new();

    // Zero flat fee / zero 1x rate / zero margin so Price == Cost, keeping the numbers verifiable.
    private static GatewayCost SimulatedGatewayCost() =>
        GatewayCost.Create(
            GatewayProvider.Of("Simulated"),
            flatFeePerTransaction: 0m,
            avistaRatePercent: 2.5m,
            new Dictionary<int, decimal> { [1] = 0m, [2] = 5m });

    private PricingHighlights CreateSut() =>
        new(_gatewayCostRepository.Object, _campaignRepository.Object,
            new InstallmentOptions { ActiveProvider = "Simulated", MinMarginPercent = 0m });

    [Fact]
    public async Task ComputeAsync_ShouldApplyTheDiscount_WhenACampaignIsInForce()
    {
        var productId = Guid.NewGuid();
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Percentage, 10m));

        var breakdown = await CreateSut().ComputeAsync(productId, cost: 100m, nominalPrice: 200m);

        Assert.Equal(90m, breakdown.Price);
    }

    // This is what makes an ended or expired campaign roll the catalog price back: the publisher
    // re-reads on a product-discounts REMOVE, finds nothing, and republishes the full price.
    [Fact]
    public async Task ComputeAsync_ShouldPublishTheUndiscountedPrice_WhenNoCampaignIsInForce()
    {
        var productId = Guid.NewGuid();
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveDiscount?)null);

        var breakdown = await CreateSut().ComputeAsync(productId, cost: 100m, nominalPrice: 200m);

        Assert.Equal(100m, breakdown.Price);
    }

    [Fact]
    public async Task ComputeAsync_ShouldReturnZeroedHighlights_WhenNoGatewayProviderIsConfigured()
    {
        // Zeroed rather than skipped, so the price itself still syncs to the search document.
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GatewayCost?)null);

        var breakdown = await CreateSut().ComputeAsync(Guid.NewGuid(), cost: 100m, nominalPrice: 200m);

        Assert.Equal(0m, breakdown.Price);
        Assert.Equal(0m, breakdown.CashPrice);
        Assert.Empty(breakdown.InstallmentPlan);
    }
}
