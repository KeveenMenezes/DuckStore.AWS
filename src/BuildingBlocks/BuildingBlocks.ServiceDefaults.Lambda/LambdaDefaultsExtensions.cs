using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.ServiceDefaults.Lambda;

/// <summary>
/// Lambda-shaped subset of ServiceDefaults (ADR-0022): structured logging to CloudWatch
/// and OpenTelemetry tracing of AWS SDK calls. No Elasticsearch, health checks, or
/// service discovery — those assume a long-lived host and don't apply to Lambda.
/// </summary>
public static class LambdaDefaultsExtensions
{
    public static IServiceCollection AddLambdaDefaults(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Set by the AWS Lambda runtime; absent under the local Aspire emulator host.
        var functionName = Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME");

        services.AddLogging(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));

            if (functionName is not null)
            {
                // Single-line JSON to stdout: CloudWatch ingests one structured event per
                // line, queryable by property in Logs Insights instead of text-grepping.
                logging.AddJsonConsole(json =>
                {
                    json.IncludeScopes = true;
                    json.UseUtcTimestamp = true;
                    json.TimestampFormat = "O";
                });
            }
            else
            {
                logging.AddSimpleConsole(console => console.SingleLine = true);
            }
        });

        AddTracing(services, functionName);

        return services;
    }

    private static void AddTracing(IServiceCollection services, string? functionName)
    {
        // Traces need somewhere to go: Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT locally
        // (dashboard); on AWS it comes from an ADOT collector layer when one is attached.
        // Without an endpoint no provider is built, so tracing is a silent no-op.
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrEmpty(otlpEndpoint))
        {
            return;
        }

        var serviceName = functionName
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? AppDomain.CurrentDomain.FriendlyName;

        // AddAWSInstrumentation hooks the AWS SDK v4 TelemetryProvider globally, so
        // DynamoDB/EventBridge calls made through any injected client become spans.
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .AddAWSInstrumentation()
            .AddOtlpExporter()
            .Build();

        // Singleton so the provider stays alive (and flushing) for the container lifetime.
        services.AddSingleton(tracerProvider);
    }
}
