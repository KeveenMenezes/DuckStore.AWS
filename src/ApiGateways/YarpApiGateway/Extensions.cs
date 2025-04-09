namespace YarpApiGateway;

public static class Extensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddServiceDiscoveryCore();
        services.AddHttpForwarderWithServiceDiscovery();

        services.AddHealthChecks()
            .AddUrlGroup(new Uri("http://catalog-api/health"), name: "catalogapi-check");

        return services;
    }
}