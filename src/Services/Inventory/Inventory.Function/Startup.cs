using BuildingBlocks.ServiceDefaults.Lambda;
using Inventory.Function.Shared.Configuration;

namespace Inventory.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLambdaDefaults(configuration);
        services.AddInventoryServices(configuration);
    }
}
