using BuildingBlocks.ServiceDefaults.Lambda;
using Review.Function.Shared.Configuration;

namespace Review.Function;

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
        services.AddReviewServices(configuration);
    }
}
