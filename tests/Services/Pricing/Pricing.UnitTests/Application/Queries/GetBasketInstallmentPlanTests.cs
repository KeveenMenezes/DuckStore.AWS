using BuildingBlocks.Core.Validation;
using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Data;
using Pricing.Function.Modules.Prices.Domain.Entities;
using Pricing.Function.Modules.Prices.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.UnitTests.Application.Queries;

public class GetBasketInstallmentPlanTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IPriceRepository> _priceRepository;
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository;
    private readonly Mock<ICampaignRepository> _campaignRepository;
    private readonly GetBasketInstallmentPlanQueryValidator _validator;

    public GetBasketInstallmentPlanTests()
    {
        _autoMocker = new AutoMocker();
        _priceRepository = _autoMocker.GetMock<IPriceRepository>();
        _gatewayCostRepository = _autoMocker.GetMock<IGatewayCostRepository>();
        _campaignRepository = _autoMocker.GetMock<ICampaignRepository>();
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveDiscount?)null);
        _validator = new GetBasketInstallmentPlanQueryValidator();
    }

    // Zero flat fee / zero 1x rate / zero merchant margin so Price == Cost for every item, keeping
    // the numbers clean and the math easy to verify by hand.
    private static GatewayCost SimulatedGatewayCost() =>
        GatewayCost.Create(
            GatewayProvider.Of("Simulated"),
            flatFeePerTransaction: 0m,
            avistaRatePercent: 2.5m,
            new Dictionary<int, decimal> { [1] = 0m, [2] = 5m, [3] = 10m, [6] = 20m, [12] = 50m });

    private GetBasketInstallmentPlanHandler CreateHandler() =>
        new(_priceRepository.Object, _gatewayCostRepository.Object, _campaignRepository.Object,
            new InstallmentOptions { ActiveProvider = "Simulated", MinMarginPercent = 0m });

    [Fact]
    public async Task Handle_ShouldFindAUnifiedPlan_DistinctFromEitherItemsOwnPlan_WhenCartMixesMargins()
    {
        // Product A alone: price=10, originalPrice=15 -> ratio is generous enough that even the
        // 12x rate (50%) stays within budget (10 * 1.5 = 15 <= 15), so alone it allows max=12.
        var productA = Guid.NewGuid();
        // Product B alone: price=90, originalPrice=95 -> a thin buffer that clears 2x (90*1.05=94.5
        // <= 95) but not 3x (90*1.10=99 > 95), so alone it allows max=2.
        var productB = Guid.NewGuid();

        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(productA) && ids.Contains(productB)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Price.Create(ProductId.Of(productA), nominalPrice: 15m, cost: 10m),
                Price.Create(ProductId.Of(productB), nominalPrice: 95m, cost: 90m)
            ]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());

        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetBasketInstallmentPlanQuery(
                [new BasketInstallmentItem(productA, 1), new BasketInstallmentItem(productB, 1)]),
            CancellationToken.None);

        // totalCost = totalPrice = 100 (10 + 90); totalOriginalPrice = 110 (15 + 95).
        // 2x: 100*1.05=105 <= 110 (free); 3x: 100*1.10=110 <= 110 (free, exactly);
        // 6x: 100*1.20=120 > 110 (interest, latches from here on) — neither item's own plan
        // (max=12 or max=2) predicts this: the unified cart plan lands strictly in between.
        Assert.Equal(110m, result.TotalOriginalPrice);
        Assert.Equal(100m, result.Price);
        Assert.Equal(3, result.MaxInstallmentsWithoutInterest);

        Assert.False(result.InstallmentPlan.Single(e => e.Count == 3).HasInterest);
        Assert.True(result.InstallmentPlan.Single(e => e.Count == 6).HasInterest);
        Assert.True(result.InstallmentPlan.Single(e => e.Count == 12).HasInterest);
    }

    [Fact]
    public async Task Handle_ShouldWeightTotalsByQuantity()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 50m, cost: 40m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());

        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 3)]),
            CancellationToken.None);

        Assert.Equal(150m, result.TotalOriginalPrice);
        Assert.Equal(120m, result.Price);
    }

    // The bug this guards against: the cart used to ignore campaigns entirely, so the same product
    // showed a discounted price on its own page and an undiscounted one in the cart total.
    [Fact]
    public async Task Handle_ShouldApplyActiveCampaignDiscount_MatchingTheSingleProductPlanExactly()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 200m, cost: 100m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Percentage, 10m));

        var result = await CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 1)]),
            CancellationToken.None);

        // What the product page shows for the very same product, via the single-product path.
        var singleProduct = InstallmentCalculator.CalculateWithOptionalDiscount(
            cost: 100m, originalPrice: 200m, SimulatedGatewayCost(), minMarginPercent: 0m,
            valueTiers: [], DiscountValue.Of(DiscountType.Percentage, 10m));

        Assert.Equal(singleProduct.Price, result.Price);
        Assert.Equal(90m, result.Price);            // price 100 - 10%
        Assert.Equal(200m, result.TotalOriginalPrice); // sticker price is never discounted
    }

    [Fact]
    public async Task Handle_ShouldDiscountOnlyTheDiscountedItemsShareOfTheCart()
    {
        // Two items of equal nominal value; only one is on campaign, so a 20% discount on half the
        // cart must come out as 10% off the cart total — not 20%, and not nothing.
        var discounted = Guid.NewGuid();
        var fullPrice = Guid.NewGuid();

        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Price.Create(ProductId.Of(discounted), nominalPrice: 100m, cost: 50m),
                Price.Create(ProductId.Of(fullPrice), nominalPrice: 100m, cost: 50m)
            ]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(discounted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Percentage, 20m));

        var result = await CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery(
                [new BasketInstallmentItem(discounted, 1), new BasketInstallmentItem(fullPrice, 1)]),
            CancellationToken.None);

        // Undiscounted cart price = totalCost = 100; half of it carries 20% off -> 100 - 10 = 90.
        Assert.Equal(90m, result.Price);
        Assert.Equal(200m, result.TotalOriginalPrice);
    }

    [Fact]
    public async Task Handle_ShouldApplyFixedDiscountPerUnit_WhenTheSameProductIsBoughtSeveralTimes()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 200m, cost: 100m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(r => r.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Fixed, 5m));

        var result = await CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 3)]),
            CancellationToken.None);

        // Undiscounted cart price = totalCost = 300; R$5 off each of the 3 units -> 285.
        Assert.Equal(285m, result.Price);
    }

    [Fact]
    public async Task Handle_ShouldLeaveCartUntouched_WhenNoItemHasAnActiveCampaign()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 200m, cost: 100m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());

        var result = await CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 1)]),
            CancellationToken.None);

        Assert.Equal(100m, result.Price);
    }

    [Fact]
    public async Task Handle_ShouldLookUpEachProductOnlyOnce_WhenTheSameProductRepeatsAcrossLines()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 100m, cost: 50m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());

        await CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery(
                [new BasketInstallmentItem(productId, 1), new BasketInstallmentItem(productId, 2)]),
            CancellationToken.None);

        _campaignRepository.Verify(
            r => r.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenAnyProductHasNoPriceSet()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = CreateHandler();

        await Assert.ThrowsAsync<PriceNotFoundException>(
            async () => await handler.Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 1)]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenActiveProviderHasNoGatewayCostConfigured()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(productId), nominalPrice: 100m, cost: 10m)]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GatewayCost?)null);

        var handler = CreateHandler();

        await Assert.ThrowsAsync<GatewayCostNotFoundException>(
            async () => await handler.Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 1)]),
            CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenItemsIsEmpty()
    {
        var result = _validator.Validate(new GetBasketInstallmentPlanQuery([])).ToList();

        Assert.Contains(result, f => f.PropertyName == "Items");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenAnyItemHasZeroQuantity()
    {
        var result = _validator.Validate(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(Guid.NewGuid(), 0)])).ToList();

        Assert.NotEmpty(result);
    }
}
