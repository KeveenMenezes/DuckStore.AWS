namespace Review.Function.Shared.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddReviewServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEventBridgeMessaging(configuration);

        services.AddScoped<IStreamRule<ReviewStreamImage>, ReviewCreatedRule>();
        services.AddScoped<IStreamRule<ReviewStreamImage>, ReviewUpdatedRule>();
        services.AddScoped<StreamRuleDispatcher<ReviewStreamImage>>();

        return services;
    }
}
