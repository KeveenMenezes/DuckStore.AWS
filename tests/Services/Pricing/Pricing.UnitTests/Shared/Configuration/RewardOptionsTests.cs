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
            ["Rewards:ExpiryDays"] = "30"
        });

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(200, options.PointsPerUnit);
        Assert.Equal(25m, options.CurrencyPerUnit);
        Assert.Equal(30, options.ExpiryDays);
    }

    [Fact]
    public void FromConfiguration_ShouldFallBackToDefaults_WhenUnconfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(100, options.PointsPerUnit);
        Assert.Equal(10m, options.CurrencyPerUnit);
        Assert.Equal(90, options.ExpiryDays);
    }

    // PointsPerUnit is a divisor: a zero from a typo'd env var would throw DivideByZeroException
    // inside every redemption, and a negative one would mint a discount that raises the cart total.
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
        Assert.Equal(50m, options.ConvertToCurrency(500));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void FromConfiguration_ShouldFallBackToDefaults_WhenTheOtherValuesAreNotPositive(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Rewards:CurrencyPerUnit"] = configured,
            ["Rewards:ExpiryDays"] = configured
        });

        var options = RewardOptions.FromConfiguration(configuration);

        Assert.Equal(10m, options.CurrencyPerUnit);
        Assert.Equal(90, options.ExpiryDays);
    }

    [Fact]
    public void ConvertToCurrency_ShouldApplyTheRate_AndRoundToTwoDecimals()
    {
        var options = new RewardOptions { PointsPerUnit = 100, CurrencyPerUnit = 10m };

        var amount = options.ConvertToCurrency(333);

        Assert.Equal(33.30m, amount);
    }
}
