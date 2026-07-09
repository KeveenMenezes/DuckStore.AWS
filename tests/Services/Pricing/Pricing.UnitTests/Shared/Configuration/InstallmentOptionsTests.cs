using Microsoft.Extensions.Configuration;
using Pricing.Function.Shared.Configuration;

namespace Pricing.UnitTests.Shared.Configuration;

public class InstallmentOptionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void FromConfiguration_ShouldParseValueTiers_SortedAscendingByMinAmount()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Installments:ValueTiers:0:MinAmount"] = "3000",
            ["Installments:ValueTiers:0:MaxInstallments"] = "10",
            ["Installments:ValueTiers:1:MinAmount"] = "1000",
            ["Installments:ValueTiers:1:MaxInstallments"] = "6"
        });

        var options = InstallmentOptions.FromConfiguration(configuration);

        Assert.Equal(2, options.ValueTiers.Count);
        Assert.Equal(1000m, options.ValueTiers[0].MinAmount);
        Assert.Equal(6, options.ValueTiers[0].MaxInstallments);
        Assert.Equal(3000m, options.ValueTiers[1].MinAmount);
        Assert.Equal(10, options.ValueTiers[1].MaxInstallments);
    }

    [Fact]
    public void FromConfiguration_ShouldStopAtFirstGap_NotSkipOverIt()
    {
        // Index 0 is missing entirely; index 1 is fully configured. A gap is treated as the end of
        // the list, not skipped over, so only a contiguous 0..N run is honored.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Installments:ValueTiers:1:MinAmount"] = "1000",
            ["Installments:ValueTiers:1:MaxInstallments"] = "6"
        });

        var options = InstallmentOptions.FromConfiguration(configuration);

        Assert.Empty(options.ValueTiers);
    }

    [Fact]
    public void FromConfiguration_ShouldFallBackToEmptyList_WhenNoTiersConfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var options = InstallmentOptions.FromConfiguration(configuration);

        Assert.Empty(options.ValueTiers);
    }

    [Fact]
    public void FromConfiguration_ShouldStopAtMalformedEntry()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Installments:ValueTiers:0:MinAmount"] = "1000",
            ["Installments:ValueTiers:0:MaxInstallments"] = "6",
            ["Installments:ValueTiers:1:MinAmount"] = "not-a-number",
            ["Installments:ValueTiers:1:MaxInstallments"] = "10"
        });

        var options = InstallmentOptions.FromConfiguration(configuration);

        Assert.Single(options.ValueTiers);
        Assert.Equal(1000m, options.ValueTiers[0].MinAmount);
    }
}
