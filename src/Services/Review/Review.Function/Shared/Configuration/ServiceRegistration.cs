namespace Review.Function.Shared.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddReviewServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEventBridgeMessaging(configuration);
        return services;
    }
}
