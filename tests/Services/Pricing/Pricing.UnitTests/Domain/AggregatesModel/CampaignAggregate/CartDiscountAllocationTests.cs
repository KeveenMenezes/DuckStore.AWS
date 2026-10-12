using Pricing.Function.Modules.Campaigns.Domain.Enums;
using Pricing.Function.Modules.Campaigns.Domain.Services;
using Pricing.Function.Modules.Campaigns.Domain.ValueObjects;

namespace Pricing.UnitTests.Domain.AggregatesModel.CampaignAggregate;

public class CartDiscountAllocationTests
{
    private static DiscountedCartLine Line(decimal unitNominalPrice, int quantity, DiscountType type, decimal amount) =>
        new(unitNominalPrice, quantity, DiscountValue.Of(type, amount));

    [Fact]
    public void ShouldReduceByTheFullDiscount_WhenOneLineIsTheWholeCart()
    {
        // The single-unit, single-product cart: the line's claim is the entire cart price, so the
        // reduction must be exactly what the discount takes off that price on the product page.
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m, cartNominalValue: 200m, [Line(200m, 1, DiscountType.Percentage, 10m)]);

        Assert.Equal(10m, reduction);
    }

    [Fact]
    public void ShouldReduceProportionally_WhenOnlyPartOfTheCartIsOnCampaign()
    {
        // Two lines of equal nominal value, one at 20% off: 20% of half the cart is 10% of the cart.
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m,
            cartNominalValue: 200m,
            [Line(100m, 1, DiscountType.Percentage, 20m)]);

        Assert.Equal(10m, reduction);
    }

    [Fact]
    public void ShouldApplyFixedDiscountOncePerUnit_NotOncePerLine()
    {
        // Three units of the only product in the cart, R$5 off each -> R$15, not R$5. A fixed
        // discount is quoted per unit on the product page, so the cart must honour it per unit.
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 300m, cartNominalValue: 300m, [Line(100m, 3, DiscountType.Fixed, 5m)]);

        Assert.Equal(15m, reduction);
    }

    [Fact]
    public void ShouldNotReduceBeyondTheDiscountedUnitsOwnShare_WhenFixedAmountExceedsIt()
    {
        // A R$500 fixed discount on a unit whose share of the cart price is R$100 may only take
        // that R$100 — DiscountValue.ApplyTo floors each unit at zero, never negative.
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m, cartNominalValue: 100m, [Line(100m, 1, DiscountType.Fixed, 500m)]);

        Assert.Equal(100m, reduction);
    }

    [Fact]
    public void ShouldSumReductionsAcrossSeveralDiscountedLines()
    {
        // Two campaigned lines, each half the cart's nominal value: 10% of one half plus 20% of the
        // other = 15% of a 200 cart price = 30.
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 200m,
            cartNominalValue: 200m,
            [Line(100m, 1, DiscountType.Percentage, 10m), Line(100m, 1, DiscountType.Percentage, 20m)]);

        Assert.Equal(30m, reduction);
    }

    [Fact]
    public void ShouldReduceNothing_WhenNoLineCarriesADiscount()
    {
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m, cartNominalValue: 200m, []);

        Assert.Equal(0m, reduction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void ShouldReduceNothing_WhenTheCartHasNoPriceToDiscount(decimal cartPrice)
    {
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice, cartNominalValue: 200m, [Line(200m, 1, DiscountType.Percentage, 10m)]);

        Assert.Equal(0m, reduction);
    }

    [Fact]
    public void ShouldReduceNothing_WhenNominalValueIsZero_RatherThanDivideByIt()
    {
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m, cartNominalValue: 0m, [Line(0m, 1, DiscountType.Percentage, 10m)]);

        Assert.Equal(0m, reduction);
    }

    [Fact]
    public void ShouldNeverReduceMoreThanTheCartPrice_EvenWhenEveryLineIsFullyDiscounted()
    {
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 100m,
            cartNominalValue: 200m,
            [Line(100m, 1, DiscountType.Percentage, 100m), Line(100m, 1, DiscountType.Percentage, 100m)]);

        Assert.Equal(100m, reduction);
    }

    private static CartLine AnyLine(decimal unitNominalPrice, int quantity, DiscountValue? discount = null) =>
        new(unitNominalPrice, quantity, discount);

    [Fact]
    public void LineValuesFrom_ShouldSplitTheCartPriceByNominalValue_WhenNoLineIsOnCampaign()
    {
        // Nominal 50 + 150 = 200; a cart price of 100 splits 25 / 75.
        var values = CartDiscountAllocation.LineValuesFrom(
            cartPrice: 100m, cartNominalValue: 200m, [AnyLine(50m, 1), AnyLine(150m, 1)]);

        Assert.Equal([25m, 75m], values);
    }

    [Fact]
    public void LineValuesFrom_ShouldTakeEachLinesDiscountOffItsOwnShareOnly()
    {
        // Two equal halves of a 100 cart, the first at 20% off: 50 - 10 = 40, and 50 untouched.
        var values = CartDiscountAllocation.LineValuesFrom(
            cartPrice: 100m,
            cartNominalValue: 200m,
            [AnyLine(100m, 1, DiscountValue.Of(DiscountType.Percentage, 20m)), AnyLine(100m, 1)]);

        Assert.Equal([40m, 50m], values);
    }

    [Fact]
    public void LineValuesFrom_ShouldAddUpToTheCartPriceMinusTotalReductionFrom()
    {
        // The per-line split and the cart-level reduction are the same policy: they must agree.
        var discount = DiscountValue.Of(DiscountType.Fixed, 5m);
        var lines = new[] { AnyLine(100m, 3, discount), AnyLine(40m, 2) };

        var values = CartDiscountAllocation.LineValuesFrom(cartPrice: 300m, cartNominalValue: 380m, lines);
        var reduction = CartDiscountAllocation.TotalReductionFrom(
            cartPrice: 300m, cartNominalValue: 380m, [new DiscountedCartLine(100m, 3, discount)]);

        Assert.Equal(300m - reduction, values.Sum(), precision: 10);
    }

    [Fact]
    public void LineValuesFrom_ShouldReturnZeros_WhenTheCartHasNoNominalValue()
    {
        var values = CartDiscountAllocation.LineValuesFrom(
            cartPrice: 100m, cartNominalValue: 0m, [AnyLine(0m, 1)]);

        Assert.Equal([0m], values);
    }
}
