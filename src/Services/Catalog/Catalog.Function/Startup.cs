using BuildingBlocks.ServiceDefaults.Lambda;

namespace Catalog.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLambdaDefaults(configuration);
        services.AddCatalogServices(configuration);
    }
}
