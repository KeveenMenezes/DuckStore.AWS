using Aspire.Hosting.ApplicationModel;
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
            //options.KnownProxies.Add(IPAddress.Parse("IP_DO_PROXY"));
        });
        return builder;
    }

    /// <summary>
    /// Região + credenciais dummy para dev local: o DynamoDB Local ignora as credenciais,
    /// e o cliente EventBridge (bus só na AWS) precisa de uma região para ser construído —
    /// o publish é best-effort e falha silenciosamente fora da AWS.
    /// </summary>
    public static IResourceBuilder<T> WithAwsDevEnvironment<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment =>
        builder
            .WithEnvironment("AWS_ACCESS_KEY_ID", "dummy")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "dummy")
            .WithEnvironment("AWS_REGION", "us-east-1");
}
