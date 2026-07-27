using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Extensions;

public static class Extensions
{
    /// <summary>
    /// Adds a hook to set the ASPNETCORE_FORWARDEDHEADERS_ENABLED environment variable to true for all projects in the application.
    /// </summary>
    public static IDistributedApplicationBuilder AddForwardedHeaders(this IDistributedApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            //options.KnownProxies.Add(IPAddress.Parse("PROXY_IP"));
        });
        return builder;
    }

    /// <summary>
    /// Builds the handler string Aspire's Lambda emulator resolves by reflection.
    /// </summary>
    /// <remarks>
    /// Every *.Function project builds as `bootstrap` (ADR-0042 §2), so the assembly part is no
    /// longer the project name. Spelling it out at each of the 24 call sites is what silently
    /// broke eight of them during that migration: the emulator's wrapper fails inside
    /// <c>LambdaBootstrap.InitializeAsync</c>, and the only visible symptom is a
    /// <c>RuntimeApiClientException</c> from the test tool. Composing it here makes that
    /// impossible to get wrong.
    /// </remarks>
    /// <param name="functionsNamespace">Namespace holding the generated partial class, e.g. <c>Catalog.Function</c>.</param>
    /// <param name="method">The <c>[LambdaFunction]</c> method name, e.g. <c>ProductStreamPublisher</c>.</param>
    public static string LambdaHandler(string functionsNamespace, string method) =>
        $"bootstrap::{functionsNamespace}.Functions_{method}_Generated::{method}";

    /// <summary>
    /// Region + dummy credentials for local dev: DynamoDB Local ignores the credentials, and the
    /// EventBridge client (the bus only exists on AWS) needs a region to be constructed — the
    /// publish is best-effort and fails silently outside AWS.
    /// </summary>
    public static IResourceBuilder<T> WithAwsDevEnvironment<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment =>
        builder
            .WithEnvironment("AWS_ACCESS_KEY_ID", "dummy")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "dummy")
            .WithEnvironment("AWS_REGION", "us-east-1");
}
