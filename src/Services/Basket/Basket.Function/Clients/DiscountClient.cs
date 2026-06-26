using Amazon.Lambda;
using Amazon.Lambda.Model;

namespace Basket.Function.Clients;

public record GetDiscountRequest(string ProductName);

public record GetDiscountResponse(string ProductName, string Description, int Amount);

public interface IDiscountClient
{
    Task<GetDiscountResponse> GetDiscountAsync(string productName, CancellationToken cancellationToken = default);
}

public class DiscountLambdaClient(IAmazonLambda lambdaClient, string functionName) : IDiscountClient
{
    public async Task<GetDiscountResponse> GetDiscountAsync(string productName, CancellationToken cancellationToken = default)
    {
        var response = await lambdaClient.InvokeAsync(
            new InvokeRequest
            {
                FunctionName = functionName,
                Payload = JsonSerializer.Serialize(new GetDiscountRequest(productName))
            },
            cancellationToken);

        if (response.FunctionError is not null)
            throw new InvalidOperationException($"Discount Lambda invocation failed: {response.FunctionError}");

        return JsonSerializer.Deserialize<GetDiscountResponse>(response.Payload)!;
    }
}
