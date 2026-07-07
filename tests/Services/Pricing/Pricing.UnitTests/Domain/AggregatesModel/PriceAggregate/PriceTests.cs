namespace Pricing.UnitTests.Domain.AggregatesModel.PriceAggregate;

public class PriceTests
{
    [Fact]
    public void Create_ShouldSucceed_WhenNominalPriceAndCostArePositive()
    {
        var price = Price.Create(ProductId.Of(Guid.NewGuid()), 29.90m, 15.00m);

        Assert.Equal(29.90m, price.NominalPrice);
        Assert.Equal(15.00m, price.Cost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldThrow_WhenNominalPriceIsNotPositive(decimal nominalPrice)
    {
        Assert.Throws<PriceBadRequestException>(
            () => Price.Create(ProductId.Of(Guid.NewGuid()), nominalPrice, 15.00m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldThrow_WhenCostIsNotPositive(decimal cost)
    {
        Assert.Throws<PriceCostBadRequestException>(
            () => Price.Create(ProductId.Of(Guid.NewGuid()), 29.90m, cost));
    }

    [Fact]
    public void Update_ShouldChangeNominalPriceAndCost_WhenValuesArePositive()
    {
        var price = Price.Create(ProductId.Of(Guid.NewGuid()), 29.90m, 15.00m);

        price.Update(39.90m, 20.00m);

        Assert.Equal(39.90m, price.NominalPrice);
        Assert.Equal(20.00m, price.Cost);
    }

    [Fact]
    public void Update_ShouldThrow_WhenNominalPriceIsNotPositive()
    {
        var price = Price.Create(ProductId.Of(Guid.NewGuid()), 29.90m, 15.00m);

        Assert.Throws<PriceBadRequestException>(() => price.Update(0, 15.00m));
    }

    [Fact]
    public void Update_ShouldThrow_WhenCostIsNotPositive()
    {
        var price = Price.Create(ProductId.Of(Guid.NewGuid()), 29.90m, 15.00m);

        Assert.Throws<PriceCostBadRequestException>(() => price.Update(29.90m, 0));
    }
}
