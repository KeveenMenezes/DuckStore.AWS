using BuildingBlocks.ServiceDefaults.Lambda;
using Ordering.Function.Shared.Configuration;

namespace Ordering.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        // Registered so [LambdaFunction] methods can inject IConfiguration via [FromServices]
        // (the stream publisher reads the OrderFulfillment feature gate from it).
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLambdaDefaults(configuration);
        services.AddOrderingServices(configuration);
    }
}
