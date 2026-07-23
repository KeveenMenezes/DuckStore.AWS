using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Data;
using Pricing.Function.Modules.Prices.Domain.Entities;
using Pricing.Function.Modules.Prices.Domain.ValueObjects;
using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Shared.Configuration;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.UnitTests.Application.Queries;

public class GetBasketInstallmentPlanTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IPriceRepository> _priceRepository;
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository;
    private readonly GetBasketInstallmentPlanQueryValidator _validator;

    public GetBasketInstallmentPlanTests()
    {
        _autoMocker = new AutoMocker();
        _priceRepository = _autoMocker.GetMock<IPriceRepository>();
        _gatewayCostRepository = _autoMocker.GetMock<IGatewayCostRepository>();
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
        new(_priceRepository.Object, _gatewayCostRepository.Object,
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

    [Fact]
    public async Task Handle_ShouldThrow_WhenAnyProductHasNoPriceSet()
    {
        var productId = Guid.NewGuid();
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = CreateHandler();

        await Assert.ThrowsAsync<PriceNotFoundException>(() => handler.Handle(
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

        await Assert.ThrowsAsync<GatewayCostNotFoundException>(() => handler.Handle(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(productId, 1)]),
            CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenItemsIsEmpty()
    {
        var result = _validator.TestValidate(new GetBasketInstallmentPlanQuery([]));

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenAnyItemHasZeroQuantity()
    {
        var result = _validator.TestValidate(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(Guid.NewGuid(), 0)]));

        Assert.False(result.IsValid);
    }
}
