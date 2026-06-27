using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Catalog.StreamEventPublish.Lambda;

// Triggered by the Products DynamoDB Stream. Fires one POST to the Next.js webhook per batch,
// causing revalidateTag('products') to run and clearing the ISR cache for product pages.
public class Function
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly string _webhookSecret;

    public Function()
    {
        _webhookUrl = Environment.GetEnvironmentVariable("Catalog__WebhookUrl")
            ?? throw new InvalidOperationException("Catalog__WebhookUrl is not configured");
        _webhookSecret = Environment.GetEnvironmentVariable("CATALOG_WEBHOOK_SECRET")
            ?? throw new InvalidOperationException("CATALOG_WEBHOOK_SECRET is not configured");

        _httpClient = new HttpClient();
    }

    public async Task FunctionHandler(DynamoDBEvent dynamoEvent)
    {
        if (dynamoEvent.Records.Count == 0)
            return;

        using var request = new HttpRequestMessage(HttpMethod.Post, _webhookUrl);
        request.Headers.Add("x-webhook-secret", _webhookSecret);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                Console.Error.WriteLine($"[catalog-stream] Webhook responded with {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[catalog-stream] Webhook call failed: {ex.Message}");
        }
    }

    private static async Task Main()
    {
        Func<DynamoDBEvent, Task> handler = new Function().FunctionHandler;

        using var bootstrap = LambdaBootstrapBuilder.Create(
                handler,
                new DefaultLambdaJsonSerializer())
            .Build();

        await bootstrap.RunAsync();
    }
}
