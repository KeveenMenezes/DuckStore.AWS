[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Discount.Lambda.Functions;

public class DiscountFunctions
{
    private readonly IHost _host;
    private readonly TracerProvider _traceProvider;

    /// <summary>
    /// Default constructor that Lambda will invoke.
    /// </summary>
    public DiscountFunctions()
    {
        var builder = Host.CreateApplicationBuilder();

        // Add service defaults (OpenTelemetry, logging, etc.)
        builder.AddServiceDefaults();

        // Add Redis client
        builder.AddRedisClient("redis");

        // Add AWS DynamoDB services
        builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddScoped<IDynamoDBContext, DynamoDBContext>();

        _host = builder.Build();
        _traceProvider = _host.Services.GetRequiredService<TracerProvider>();
    }

    [LambdaFunction]
    [RestApi(LambdaHttpMethod.Get, "/discount/{id}")]
    public IHttpResult GetDiscount(string id, ILambdaContext context)
    {
        context.Logger.LogInformation($"Getting discount for ID: {id}");

        // TODO: Implementar lógica para buscar o cupom do DynamoDB/Redis
        var coupon = Coupon.CreateNoDiscountCoupon();
        return HttpResults.Ok(coupon);
    }

    [LambdaFunction]
    [RestApi(LambdaHttpMethod.Post, "/discount")]
    public IHttpResult CreateDiscount([FromBody] CreateDiscountRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Creating discount with code: {request.Code}");

        // TODO: Implementar lógica para criar o cupom no DynamoDB
        var coupon = Coupon.CreateNoDiscountCoupon();
        return HttpResults.Ok(coupon);
    }

    [LambdaFunction]
    [RestApi(LambdaHttpMethod.Put, "/discount/{id}")]
    public IHttpResult UpdateDiscount(string id, [FromBody] UpdateDiscountRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Updating discount ID: {id}");

        // TODO: Implementar lógica para atualizar o cupom no DynamoDB
        var coupon = Coupon.CreateNoDiscountCoupon();
        return HttpResults.Ok(coupon);
    }

    [LambdaFunction]
    [RestApi(LambdaHttpMethod.Delete, "/discount/{id}")]
    public IHttpResult DeleteDiscount(string id, ILambdaContext context)
    {
        context.Logger.LogInformation($"Deleting discount ID: {id}");

        // TODO: Implementar lógica para deletar o cupom do DynamoDB
        var response = new DeleteDiscountResponse { Success = true };
        return HttpResults.Ok(response);
    }
}
