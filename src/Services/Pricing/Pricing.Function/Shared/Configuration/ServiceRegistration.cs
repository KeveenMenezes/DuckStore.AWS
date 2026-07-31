using BuildingBlocks.Core.Validation;
using Pricing.Function.Modules.Campaigns.Features.CreateCampaign;
using Pricing.Function.Modules.Campaigns.Features.EndCampaign;
using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Modules.Prices.Features.GetInstallmentPlan;
namespace Pricing.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (Lambda-backed
// mutations/queries), the EventBridge consumer, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddPricingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        var assembly = typeof(ServiceRegistration).Assembly;
        // Mediator generates the dispatch table at compile time; AddMediator() is the
        // generated registration, so no assembly is scanned at startup (ADR-0042 §7).
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // Explicit, because there is no scanning left to discover them —
        // the point of dropping FluentValidation (ADR-0042 §7).
        services.AddScoped<IValidator<GetInstallmentPlanQuery>, GetInstallmentPlanQueryValidator>();
        services.AddScoped<IValidator<GetBasketInstallmentPlanQuery>, GetBasketInstallmentPlanQueryValidator>();
        services.AddScoped<IValidator<CreateCampaignCommand>, CreateCampaignCommandValidator>();
        services.AddScoped<IValidator<EndCampaignCommand>, EndCampaignCommandValidator>();

        services.AddSingleton(InstallmentOptions.FromConfiguration(configuration));

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IPriceRepository, DynamoPriceRepository>();
        services.AddScoped<ICampaignRepository, DynamoCampaignRepository>();
        services.AddScoped<IGatewayCostRepository, DynamoGatewayCostRepository>();
        // Shared by both CDC stream publishers — a price write and a campaign write need the same
        // recomputed payment highlights (ADR-0044).
        services.AddScoped<PricingHighlights>();

        services.AddEventBridgeMessaging(configuration);
        services.AddIdempotentEventConsumer(ProcessedIntegrationEvent.TableName);

        return services;
    }
}
