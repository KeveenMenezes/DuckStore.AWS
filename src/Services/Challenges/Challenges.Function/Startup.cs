using BuildingBlocks.ServiceDefaults.Lambda;
using Challenges.Function.Shared.Configuration;

namespace Challenges.Function;

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
        services.AddChallengesServices(configuration);
    }
}
