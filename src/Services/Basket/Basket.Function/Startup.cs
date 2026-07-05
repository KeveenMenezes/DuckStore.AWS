using Basket.Function.Shared.Configuration;
using BuildingBlocks.ServiceDefaults.Lambda;

namespace Basket.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Aspire injects the configuration via environment variables (ConnectionStrings__*, services__*).
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLambdaDefaults(configuration);
        services.AddBasketServices(configuration);
    }
}
