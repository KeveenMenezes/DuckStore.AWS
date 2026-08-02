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

    // One instance serves one Streams batch (Scoped, one scope per invocation), and every record in
    // it asks for the same active provider — so the gateway cost is read once, not once per record.
    [Fact]
    public async Task ComputeAsync_ShouldReadTheGatewayCostOnce_AcrossEveryRecordOfABatch()
    {
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        var sut = CreateSut();

        await sut.ComputeAsync(Guid.NewGuid(), cost: 100m, nominalPrice: 200m);
        await sut.ComputeAsync(Guid.NewGuid(), cost: 50m, nominalPrice: 80m);
        await sut.ComputeAsync(Guid.NewGuid(), cost: 10m, nominalPrice: 20m);

        _gatewayCostRepository.Verify(
            r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()), Times.Once);
    }

    // "No provider configured" has to be cached like any other answer, or the miss path re-reads on
    // every record — the exact cost this memoization exists to remove.
    [Fact]
    public async Task ComputeAsync_ShouldReadOnce_AcrossABatch_EvenWhenNoProviderIsConfigured()
    {
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GatewayCost?)null);
        var sut = CreateSut();

        await sut.ComputeAsync(Guid.NewGuid(), cost: 100m, nominalPrice: 200m);
        await sut.ComputeAsync(Guid.NewGuid(), cost: 50m, nominalPrice: 80m);

        _gatewayCostRepository.Verify(
            r => r.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The per-product discount is not cached: two records in the same batch are two different
    // products, and reusing one product's campaign for another would publish a wrong price.
    [Fact]
    public async Task ComputeAsync_ShouldReadTheDiscount_ForEveryProductInTheBatch()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(first, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Percentage, 10m));
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(second, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveDiscount?)null);
        var sut = CreateSut();

        var discounted = await sut.ComputeAsync(first, cost: 100m, nominalPrice: 200m);
        var undiscounted = await sut.ComputeAsync(second, cost: 100m, nominalPrice: 200m);

        Assert.Equal(90m, discounted.Price);
        Assert.Equal(100m, undiscounted.Price);
    }
}
