using BuildingBlocks.Messaging.EventBridge;
using Catalog.Function.Modules.Products.EventsIntegration.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catalog.Function.Modules.Products.EventsIntegration.Consumer;

// Triggered by CatalogUpdatedEvent on EventBridge. Fires one POST to the Next.js webhook to
// trigger ISR cache revalidation (revalidateTag('products')).
public class CatalogUpdatedConsumerFunction
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _webhookUrl;
    private readonly string _webhookSecret;

    public CatalogUpdatedConsumerFunction()
    {
        _webhookUrl = Environment.GetEnvironmentVariable("Catalog__WebhookUrl")
            ?? throw new InvalidOperationException("Catalog__WebhookUrl is not configured");
        _webhookSecret = Environment.GetEnvironmentVariable("CATALOG_WEBHOOK_SECRET")
            ?? throw new InvalidOperationException("CATALOG_WEBHOOK_SECRET is not configured");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task FunctionHandler(EventBridgeEvent<CatalogUpdatedEvent> evt)
    {
        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogUpdatedConsumerFunction>>();
        var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, _webhookUrl);
        request.Headers.Add("x-webhook-secret", _webhookSecret);
        request.Headers.Add("x-request-id", evt.Id);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("[catalog-updated-consumer] Webhook responded with {StatusCode}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[catalog-updated-consumer] Webhook call failed");
        }
    }
}
