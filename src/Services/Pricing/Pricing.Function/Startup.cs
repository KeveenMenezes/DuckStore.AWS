using BuildingBlocks.ServiceDefaults.Lambda;
using Pricing.Function.Shared.Configuration;

namespace Pricing.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        // Registered so [LambdaFunction] methods can inject IConfiguration via [FromServices]
        // (GetInstallmentPlan reads the Installments:* fee/margin settings from it).
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLambdaDefaults(configuration);
        services.AddPricingServices(configuration);
    }
}
