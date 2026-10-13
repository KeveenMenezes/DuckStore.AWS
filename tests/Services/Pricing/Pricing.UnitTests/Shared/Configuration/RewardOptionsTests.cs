using System.Globalization;
using Microsoft.Extensions.Configuration;
using Pricing.Function.Shared.Configuration;

namespace Pricing.UnitTests.Shared.Configuration;

public class RewardOptionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void FromConfiguration_ShouldParseTheConfiguredRate()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Rewards:PointsPerUnit"] = "200",
            ["Rewards:CurrencyPerUnit"] = "25",
            ["Rewards:MaxDiscountPercent"] = "15"
        });

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(200, options.PointsPerUnit);
        Assert.Equal(25m, options.CurrencyPerUnit);
        Assert.Equal(15m, options.MaxDiscountPercent);
    }

    [Fact]
    public void FromConfiguration_ShouldFallBackToTheBusinessRules_WhenUnconfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var options = RewardOptions.FromConfiguration(configuration);

        // 100 points = R$1, at most 20% of the order (ADR-0048 §3).
        Assert.Equal(100, options.PointsPerUnit);
        Assert.Equal(1m, options.CurrencyPerUnit);
        Assert.Equal(20m, options.MaxDiscountPercent);
    }

    // PointsPerUnit is a divisor: a zero from a typo'd env var would throw DivideByZeroException
    // inside every quote, and a negative one would mint a discount that raises the cart total.
    // Parsing is not the same as being usable.
    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void FromConfiguration_ShouldFallBackToTheDefaultRate_WhenPointsPerUnitIsNotPositive(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Rewards:PointsPerUnit"] = configured
        });

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(100, options.PointsPerUnit);
        Assert.Equal(5m, options.PointsDiscount(500));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void FromConfiguration_ShouldFallBackToDefaults_WhenTheOtherValuesAreNotPositive(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Rewards:CurrencyPerUnit"] = configured,
            ["Rewards:MaxDiscountPercent"] = configured
        });

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(1m, options.CurrencyPerUnit);
        Assert.Equal(20m, options.MaxDiscountPercent);
    }

    // Above 100% the cap would let points pay more than the cart; that's a typo, not a policy.
    [Fact]
    public void FromConfiguration_ShouldFallBackToTheDefaultCap_WhenItIsAbove100Percent()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Rewards:MaxDiscountPercent"] = "120"
        });

        Assert.Equal(20m, RewardOptions.FromConfiguration(configuration).MaxDiscountPercent);
    }

    [Theory]
    [InlineData("200.00", 4000)]
    [InlineData("0.99", 19)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    public void MaxRedeemablePoints_ShouldBe20PercentOfTheTotal_InWholePoints(string total, int expected)
    {
        var options = new RewardOptions();

        Assert.Equal(expected, options.MaxRedeemablePoints(decimal.Parse(total, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(1000, "10.00")]
    [InlineData(150, "1.50")]
    [InlineData(1, "0.01")]
    [InlineData(0, "0")]
    public void PointsDiscount_ShouldConvertAt100PointsPerReal(int points, string expected)
    {
        var options = new RewardOptions();

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), options.PointsDiscount(points));
    }

    // A rate that doesn't divide evenly into cents rounds down: the store never gives away more
    // than the points are worth.
    [Fact]
    public void PointsDiscount_ShouldRoundDown_ToWholeCents()
    {
        var options = new RewardOptions { PointsPerUnit = 300, CurrencyPerUnit = 1m };

        Assert.Equal(0.33m, options.PointsDiscount(100));
    }
}
