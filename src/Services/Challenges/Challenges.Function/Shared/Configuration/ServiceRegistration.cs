using Challenges.Function.Modules.Progress.Features.RedeemPoints;
using Challenges.Function.Modules.Progress.Features.RevealHint;
using Challenges.Function.Modules.Progress.Features.SubmitAnswer;

namespace Challenges.Function.Shared.Configuration;

// Single DI composition for the whole service, shared by the [LambdaStartup] (Lambda-backed
// mutations), the stream-publisher Lambda, and the dev data seeder.
public static class ServiceRegistration
{
    public static IServiceCollection AddChallengesServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();

        // Mediator generates the dispatch table at compile time; AddMediator() is the
        // generated registration, so no assembly is scanned at startup (ADR-0042 §7).
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // DynamoDB Local injects AWS_ENDPOINT_URL_DYNAMODB; the SDK resolves it on its own.
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddScoped<IQuestionRepository, DynamoQuestionRepository>();
        services.AddScoped<IPlayerProgressRepository, DynamoPlayerProgressRepository>();

        // Explicit, because there is no scanning left to discover them — the point of dropping
        // FluentValidation (ADR-0042 §7).
        services.AddScoped<IValidator<SubmitAnswerCommand>, SubmitAnswerCommandValidator>();
        services.AddScoped<IValidator<RevealHintCommand>, RevealHintCommandValidator>();
        services.AddScoped<IValidator<RedeemPointsCommand>, RedeemPointsCommandValidator>();

        services.AddEventBridgeMessaging(configuration);
        services.AddScoped<IStreamRule<ProgressStreamImage>, ChallengeAnsweredRule>();
        services.AddScoped<IStreamRule<ProgressStreamImage>, PointsRedeemedRule>();
        services.AddScoped<StreamRuleDispatcher<ProgressStreamImage>>();

        return services;
    }
}
