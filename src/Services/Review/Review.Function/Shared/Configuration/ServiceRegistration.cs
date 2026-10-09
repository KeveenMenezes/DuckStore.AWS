using Amazon.DynamoDBv2;
using Review.Function.Modules.Reviews.Data;
using Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

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

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IReviewEligibilityRepository, DynamoReviewEligibilityRepository>();
        services.AddScoped<OrderCompletedHandler>();

        return services;
    }
}
