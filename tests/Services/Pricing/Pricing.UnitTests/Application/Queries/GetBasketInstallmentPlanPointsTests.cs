using BuildingBlocks.Core.Exceptions;
using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;
using Pricing.Function.Modules.CustomerDiscounts.Data;
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

// Points redemption in the cart quote and the server-side charged total (SPEC-cart-points-redemption
// §1, ADR-0048 §3): 100 points = R$1, capped at 20% of the post-campaign cart price, and the
// checkout's total is the one Pricing computes for the chosen payment method, never the client's.
public class GetBasketInstallmentPlanPointsTests
{
    private const int Credit = 2;
    private const int Cash = 3;

    private readonly Mock<IPriceRepository> _priceRepository = new();
    private readonly Mock<IGatewayCostRepository> _gatewayCostRepository = new();
    private readonly Mock<ICampaignRepository> _campaignRepository = new();
    private readonly Mock<ICustomerDiscountRepository> _customerDiscountRepository = new();
    private readonly Guid _productId = Guid.NewGuid();

    public GetBasketInstallmentPlanPointsTests()
    {
        ActiveCampaigns([]);
        _gatewayCostRepository
            .Setup(r => r.GetByProviderAsync("Simulated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GatewayCost.Create(
                GatewayProvider.Of("Simulated"),
                flatFeePerTransaction: 0m,
                avistaRatePercent: 2.5m,
                new Dictionary<int, decimal> { [1] = 0m, [2] = 5m, [3] = 10m, [6] = 20m, [12] = 50m }));
    }

    private void ActiveCampaigns(Dictionary<Guid, ActiveDiscount> discountsByProductId) =>
        _campaignRepository
            .Setup(r => r.GetActiveDiscountsForProductsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(discountsByProductId);

    // Zero flat fee, zero 1x rate and zero margin: the cart price equals the cost.
    private void CartCosting(decimal cost, decimal nominalPrice) =>
        _priceRepository
            .Setup(r => r.GetByProductIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Price.Create(ProductId.Of(_productId), nominalPrice, cost)]);

    private GetBasketInstallmentPlanHandler CreateHandler() =>
        new(_priceRepository.Object, _gatewayCostRepository.Object, _campaignRepository.Object,
            _customerDiscountRepository.Object,
            new InstallmentOptions { ActiveProvider = "Simulated", MinMarginPercent = 0m },
            new RewardOptions());

    private Task<GetBasketInstallmentPlanResult> Quote(
        int pointsToUse = 0, decimal? expectedTotal = null, int? paymentMethod = null, int? installments = null) =>
        CreateHandler().Handle(
            new GetBasketInstallmentPlanQuery(
                [new BasketInstallmentItem(_productId, 1)],
                PointsToUse: pointsToUse,
                ExpectedTotal: expectedTotal,
                PaymentMethod: paymentMethod,
                Installments: installments),
            CancellationToken.None).AsTask();

    [Fact]
    public async Task Handle_ShouldCapRedeemablePoints_At20PercentOfTheCartPrice()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var result = await Quote();

        Assert.Equal(200m, result.Price);
        Assert.Equal(4000, result.MaxRedeemablePoints); // R$200 x 20% = R$40 = 4000 points
        Assert.Equal(0m, result.PointsDiscount);
        Assert.Null(result.ChargedTotal);
    }

    [Fact]
    public async Task Handle_ShouldRoundTheCapDown_ToWholePoints()
    {
        CartCosting(cost: 0.99m, nominalPrice: 1m);

        var result = await Quote();

        Assert.Equal(19, result.MaxRedeemablePoints); // 0.99 x 20% x 100 = 19.8
    }

    [Fact]
    public async Task Handle_ShouldApplyTheCap_ToThePostCampaignPrice()
    {
        CartCosting(cost: 200m, nominalPrice: 200m);
        ActiveCampaigns(new Dictionary<Guid, ActiveDiscount> { [_productId] = new(DiscountType.Percentage, 10m) });

        var result = await Quote();

        // The campaign takes the cart below 200; the cap follows the price the customer pays.
        Assert.True(result.Price < 200m);
        Assert.Equal((int)Math.Floor(result.Price * 20m), result.MaxRedeemablePoints);
    }

    [Fact]
    public async Task Handle_ShouldSubtractThePointsDiscount_FromPriceCashPriceAndInstallments()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);
        var withoutPoints = await Quote();

        var result = await Quote(pointsToUse: 1000);

        Assert.Equal(10m, result.PointsDiscount); // 1000 points = R$10
        Assert.Equal(withoutPoints.Price - 10m, result.Price);
        Assert.Equal(withoutPoints.CashPrice - 10m, result.CashPrice);
        Assert.Equal(190m * 1.05m, result.InstallmentPlan.Single(e => e.Count == 2).TotalValue);
        // The cap is a property of the cart, not of what the customer chose to use.
        Assert.Equal(withoutPoints.MaxRedeemablePoints, result.MaxRedeemablePoints);
    }

    [Fact]
    public async Task Handle_ShouldAcceptExactlyTheCap()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var result = await Quote(pointsToUse: 4000);

        Assert.Equal(40m, result.PointsDiscount);
        Assert.Equal(160m, result.Price);
    }

    [Fact]
    public async Task Handle_ShouldReject_PointsAboveTheCap_InsteadOfTrimmingThem()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var ex = await Assert.ThrowsAsync<PointsAboveCapException>(() => Quote(pointsToUse: 4001));

        Assert.Equal(4000, ex.MaxRedeemablePoints);
        Assert.Equal("PointsAboveCap", ex.ErrorType);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheCashPrice_AsTheChargedTotal_ForCash()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);
        var quote = await Quote(pointsToUse: 1000);

        var result = await Quote(pointsToUse: 1000, expectedTotal: quote.CashPrice, paymentMethod: Cash, installments: 1);

        Assert.Equal(quote.CashPrice, result.ChargedTotal);
    }

    [Fact]
    public async Task Handle_ShouldReturnThePrice_AsTheChargedTotal_ForASingleCardInstallment()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var result = await Quote(expectedTotal: 200m, paymentMethod: Credit, installments: 1);

        Assert.Equal(200m, result.ChargedTotal);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheInstallmentTotal_AsTheChargedTotal_ForSeveralInstallments()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        // 12x carries interest here (200 x 1.50 > 250): the charged total includes it.
        var result = await Quote(expectedTotal: 300m, paymentMethod: Credit, installments: 12);

        Assert.Equal(300m, result.ChargedTotal);
    }

    [Fact]
    public async Task Handle_ShouldRejectWithPriceChanged_WhenTheExpectedTotalIsOneCentOff()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var ex = await Assert.ThrowsAsync<PriceChangedException>(
            () => Quote(expectedTotal: 199.99m, paymentMethod: Credit, installments: 1));

        Assert.Equal(200m, ex.ServerTotal);
        Assert.Equal("PriceChanged", ex.ErrorType);
    }

    [Fact]
    public async Task Handle_ShouldRejectWithPriceChanged_WhenTheClientIgnoredThePointsDiscount()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        var ex = await Assert.ThrowsAsync<PriceChangedException>(
            () => Quote(pointsToUse: 1000, expectedTotal: 200m, paymentMethod: Credit, installments: 1));

        Assert.Equal(190m, ex.ServerTotal);
    }

    [Fact]
    public async Task Handle_ShouldReject_AnInstallmentCountTheGatewayDoesNotOffer()
    {
        CartCosting(cost: 200m, nominalPrice: 250m);

        await Assert.ThrowsAsync<BadRequestException>(
            () => Quote(expectedTotal: 200m, paymentMethod: Credit, installments: 5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validator_ShouldReject_NegativePoints(int points)
    {
        var failures = new GetBasketInstallmentPlanQueryValidator().Validate(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(Guid.NewGuid(), 1)], PointsToUse: points));

        Assert.Contains(failures, f => f.PropertyName == "PointsToUse");
    }

    [Fact]
    public void Validator_ShouldReject_PointsTogetherWithACoupon()
    {
        var failures = new GetBasketInstallmentPlanQueryValidator().Validate(
            new GetBasketInstallmentPlanQuery(
                [new BasketInstallmentItem(Guid.NewGuid(), 1)],
                OwnerId: "USER#alice", DiscountId: "discount-1", PointsToUse: 100));

        Assert.Contains(failures, f => f.PropertyName == "PointsToUse");
    }

    [Fact]
    public void Validator_ShouldRequireThePaymentChoice_WhenAnExpectedTotalIsGiven()
    {
        var failures = new GetBasketInstallmentPlanQueryValidator().Validate(
            new GetBasketInstallmentPlanQuery([new BasketInstallmentItem(Guid.NewGuid(), 1)], ExpectedTotal: 10m))
            .ToList();

        Assert.Contains(failures, f => f.PropertyName == "PaymentMethod");
        Assert.Contains(failures, f => f.PropertyName == "Installments");
    }
}
