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
