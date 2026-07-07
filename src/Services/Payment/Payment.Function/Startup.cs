using BuildingBlocks.ServiceDefaults.Lambda;
using Payment.Function.Shared.Configuration;

namespace Payment.Function;

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
        services.AddPaymentServices(configuration);
    }
}
