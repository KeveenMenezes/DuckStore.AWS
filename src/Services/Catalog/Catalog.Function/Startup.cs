namespace Catalog.Function;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCatalogServices(configuration);
    }
}
