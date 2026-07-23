using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
using Pricing.Function.Shared.Configuration;

namespace Pricing.UnitTests.Application.Queries;

public class InstallmentCalculatorTests
{
    private static GatewayCost SimulatedGatewayCost(decimal flatFee = 0m, Dictionary<int, decimal>? rates = null) =>
        GatewayCost.Create(
            GatewayProvider.Of("Simulated"),
            flatFee,
            avistaRatePercent: 2.5m,
            rates ?? new Dictionary<int, decimal>
            {
                [1] = 0m,
                [2] = 2m,
                [3] = 4m,
                [12] = 20m
            });

    [Fact]
    public void Calculate_ShouldBackOutCardPriceAndCashPriceFromCostFloor()
    {
        // floor = 10 * 1.15 = 11.50; cashPrice = 11.50 + 1.00 = 12.50;
        // price = (11.50 + 1.00) / (1 - 0/100) = 12.50 (rate[1] = 0 here for a clean number)
        var gatewayCost = SimulatedGatewayCost(flatFee: 1m, rates: new Dictionary<int, decimal> { [1] = 0m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 100m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(12.50m, breakdown.CashPrice);
        Assert.Equal(12.50m, breakdown.Price);
    }

    [Fact]
    public void Calculate_ShouldBackOutCardPriceThroughRate1Fee()
    {
        // floor = 10 * 1.15 = 11.50; cashPrice = 11.50 + 1.00 = 12.50;
        // price = (11.50 + 1.00) / (1 - 10/100) = 12.50 / 0.9 = 13.888... -> 13.89
        var gatewayCost = SimulatedGatewayCost(flatFee: 1m, rates: new Dictionary<int, decimal> { [1] = 10m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 100m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(12.50m, breakdown.CashPrice);
        Assert.Equal(13.89m, breakdown.Price);
    }

    [Fact]
    public void Calculate_ShouldMarkInstallmentsInterestFree_WhileWithinOriginalPriceBuffer()
    {
        // price ends up small (~13.89); a generous originalPrice ceiling keeps every configured
        // installment count within budget.
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m,
            rates: new Dictionary<int, decimal> { [1] = 10m, [2] = 5m, [3] = 8m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 1000m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(3, breakdown.MaxInstallmentsWithoutInterest);
        Assert.All(breakdown.InstallmentPlan, e => Assert.False(e.HasInterest));
        Assert.DoesNotContain(breakdown.InstallmentPlan, e => e.Count == 1);
    }

    [Fact]
    public void Calculate_ShouldLatchInterestOn_OnceAnInstallmentCountBlowsTheOriginalPriceCeiling()
    {
        // price ~13.89. originalPrice tight enough that count=3's fee-inclusive total overflows it,
        // even though count=4's rate (deliberately non-monotonic) would have stayed under budget —
        // proving the one-way latch, not a re-check per count.
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m,
            rates: new Dictionary<int, decimal> { [1] = 10m, [2] = 1m, [3] = 50m, [4] = 1m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 15m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(2, breakdown.MaxInstallmentsWithoutInterest);

        var count2 = breakdown.InstallmentPlan.Single(e => e.Count == 2);
        var count3 = breakdown.InstallmentPlan.Single(e => e.Count == 3);
        var count4 = breakdown.InstallmentPlan.Single(e => e.Count == 4);

        Assert.False(count2.HasInterest);
        Assert.True(count3.HasInterest);
        Assert.True(count4.HasInterest); // latch stays on even though count=4's own rate is cheap
    }

    [Fact]
    public void Calculate_ShouldFallBackToMaxOne_WhenEvenCountTwoBlowsTheCeiling()
    {
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m, rates: new Dictionary<int, decimal> { [1] = 0m, [2] = 500m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 1m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(1, breakdown.MaxInstallmentsWithoutInterest);
        Assert.True(breakdown.InstallmentPlan.Single(e => e.Count == 2).HasInterest);
    }

    [Fact]
    public void Calculate_ShouldReturnEmptyPlan_WhenNoInstallmentCountsBeyondOneAreConfigured()
    {
        var gatewayCost = SimulatedGatewayCost(rates: new Dictionary<int, decimal> { [1] = 5m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 100m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        Assert.Equal(1, breakdown.MaxInstallmentsWithoutInterest);
        Assert.Empty(breakdown.InstallmentPlan);
    }

    [Fact]
    public void Calculate_TotalValueShouldAlwaysEqualValueTimesCount()
    {
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m, rates: new Dictionary<int, decimal> { [1] = 5m, [2] = 8m, [3] = 12m, [12] = 40m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 20m, originalPrice: 200m, gatewayCost, minMarginPercent: 10m, valueTiers: []);

        Assert.All(breakdown.InstallmentPlan, e => Assert.Equal(e.TotalValue, e.Value * e.Count));
    }

    [Fact]
    public void ApplyDiscount_ShouldLeaveOriginalPriceAndCashPriceUntouched_ButReducePriceAndRecomputePlan()
    {
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m, rates: new Dictionary<int, decimal> { [1] = 10m, [2] = 5m, [3] = 8m });

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 1000m, gatewayCost, minMarginPercent: 15m, valueTiers: []);

        var discount = DiscountValue.Of(DiscountType.Percentage, 10m);
        var discounted = InstallmentCalculator.ApplyDiscount(
            breakdown, originalPrice: 1000m, gatewayCost, discount, valueTiers: []);

        Assert.Equal(breakdown.CashPrice, discounted.CashPrice);
        Assert.Equal(Math.Round(breakdown.Price * 0.9m, 2), discounted.Price);
        Assert.True(discounted.Price < breakdown.Price);
        Assert.All(discounted.InstallmentPlan, e => Assert.Equal(e.TotalValue, e.Value * e.Count));
    }

    [Fact]
    public void Calculate_ShouldUnlockCountsAboveMarginLimit_WhenTierBasedLimitIsHigher()
    {
        // Same setup as the margin-latch test: price ~13.89, originalPrice=15 tight enough that
        // margin alone only clears count=2 (marginBasedLimit=2). A value tier that this basket
        // qualifies for (MinAmount <= originalPrice) promises up to count=4 instead.
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m,
            rates: new Dictionary<int, decimal> { [1] = 10m, [2] = 1m, [3] = 50m, [4] = 1m });
        var valueTiers = new List<ValueTier> { new(MinAmount: 10m, MaxInstallments: 4) };

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 15m, gatewayCost, minMarginPercent: 15m, valueTiers);

        Assert.Equal(4, breakdown.MaxInstallmentsWithoutInterest);

        var count3 = breakdown.InstallmentPlan.Single(e => e.Count == 3);
        var count4 = breakdown.InstallmentPlan.Single(e => e.Count == 4);

        // Tier-unlock only waives HasInterest — the fee-table value/total at that count is untouched.
        Assert.False(count3.HasInterest);
        Assert.False(count4.HasInterest);
        Assert.Equal(count3.Value * 3, count3.TotalValue);
        Assert.Equal(count4.Value * 4, count4.TotalValue);
    }

    [Fact]
    public void Calculate_ShouldCapTierBasedLimit_ToHighestInstallmentCountInRateTable()
    {
        // Rate table tops out at count=2, but the tier promises far more — the cap must not claim
        // a count the active provider's rate table has no fee for.
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 0m, rates: new Dictionary<int, decimal> { [1] = 0m, [2] = 5m });
        var valueTiers = new List<ValueTier> { new(MinAmount: 0m, MaxInstallments: 24) };

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 1m, gatewayCost, minMarginPercent: 15m, valueTiers);

        Assert.Equal(2, breakdown.MaxInstallmentsWithoutInterest);
        Assert.DoesNotContain(breakdown.InstallmentPlan, e => e.Count > 2);
    }

    [Fact]
    public void Calculate_ShouldIgnoreTierBasedLimit_WhenLowerThanMarginBasedLimit()
    {
        // Generous originalPrice lets margin alone reach count=3 (mirrors
        // Calculate_ShouldMarkInstallmentsInterestFree_WhileWithinOriginalPriceBuffer); a qualifying
        // tier that only promises count=1 must never pull the cap down.
        var gatewayCost = SimulatedGatewayCost(
            flatFee: 1m,
            rates: new Dictionary<int, decimal> { [1] = 10m, [2] = 5m, [3] = 8m });
        var valueTiers = new List<ValueTier> { new(MinAmount: 0m, MaxInstallments: 1) };

        var breakdown = InstallmentCalculator.Calculate(
            cost: 10m, originalPrice: 1000m, gatewayCost, minMarginPercent: 15m, valueTiers);

        Assert.Equal(3, breakdown.MaxInstallmentsWithoutInterest);
    }
}

public class GetInstallmentPlanTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IPriceRepository> _priceRepository;
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository;
    private readonly Mock<ICampaignRepository> _campaignRepository;
    private readonly GetInstallmentPlanQueryValidator _validator;

    public GetInstallmentPlanTests()
    {
        _autoMocker = new AutoMocker();
        _priceRepository = _autoMocker.GetMock<IPriceRepository>();
        _gatewayCostRepository = _autoMocker.GetMock<IGatewayCostRepository>();
        _campaignRepository = _autoMocker.GetMock<ICampaignRepository>();
        _validator = new GetInstallmentPlanQueryValidator();
    }

    private static GatewayCost SimulatedGatewayCost() =>
        GatewayCost.Create(
            GatewayProvider.Of("Simulated"),
            flatFeePerTransaction: 1m,
            avistaRatePercent: 2.5m,
            new Dictionary<int, decimal> { [1] = 0m, [12] = 0m });

    private GetInstallmentPlanHandler CreateHandler(InstallmentOptions? options = null) =>
        new(
            _priceRepository.Object,
            _gatewayCostRepository.Object,
            _campaignRepository.Object,
            options ?? new InstallmentOptions { ActiveProvider = "Simulated", MinMarginPercent = 15m });

    [Fact]
    public async Task Handle_ShouldReturnBreakdown_ForExistingPriceAndActiveProvider_WhenNoCampaignIsActive()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price.Create(ProductId.Of(productId), 1200m, 10m));
        _gatewayCostRepository
            .Setup(repo => repo.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(repo => repo.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveDiscount?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetInstallmentPlanQuery(productId), CancellationToken.None);

        Assert.Equal(productId, result.ProductId);
        Assert.Equal(1200m, result.OriginalPrice);
        Assert.Equal(12, result.MaxInstallmentsWithoutInterest);
    }

    [Fact]
    public async Task Handle_ShouldApplyActiveCampaignDiscount_ToPriceOnly()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price.Create(ProductId.Of(productId), 1200m, 10m));
        _gatewayCostRepository
            .Setup(repo => repo.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SimulatedGatewayCost());
        _campaignRepository
            .Setup(repo => repo.GetActiveDiscountForProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDiscount(DiscountType.Percentage, 10m));

        var handler = CreateHandler();

        var withoutDiscount = InstallmentCalculator.Calculate(10m, 1200m, SimulatedGatewayCost(), 15m, []);
        var result = await handler.Handle(new GetInstallmentPlanQuery(productId), CancellationToken.None);

        Assert.Equal(1200m, result.OriginalPrice);
        Assert.True(result.Price < withoutDiscount.Price);
        Assert.Equal(withoutDiscount.CashPrice, result.CashPrice);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenPriceDoesNotExist()
    {
        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Price?)null);

        var handler = CreateHandler(new InstallmentOptions());

        await Assert.ThrowsAsync<PriceNotFoundException>(
            () => handler.Handle(new GetInstallmentPlanQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenActiveProviderHasNoGatewayCostConfigured()
    {
        _priceRepository
            .Setup(repo => repo.GetByProductIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price.Create(ProductId.Of(Guid.NewGuid()), 100m, 10m));
        _gatewayCostRepository
            .Setup(repo => repo.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GatewayCost?)null);

        var handler = CreateHandler(new InstallmentOptions());

        await Assert.ThrowsAsync<GatewayCostNotFoundException>(
            () => handler.Handle(new GetInstallmentPlanQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenProductIdIsEmpty()
    {
        var result = _validator.TestValidate(new GetInstallmentPlanQuery(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }
}
