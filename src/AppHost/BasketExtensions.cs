using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;
using AppHost.Discount;
using AppHost.Extensions;

namespace AppHost.Basket;

public static class BasketExtensions
{
    public static IDistributedApplicationBuilder AddBasketLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<RedisResource> redis,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<LambdaEmulatorResource> lambdaEmulator)
    {
        builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-get-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_GetBasket_Generated::GetBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-store-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_StoreBasket_Generated::StoreBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithLambdaInvokeTarget(lambdaEmulator, DiscountExtensions.GetDiscountFunctionName)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-delete-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_DeleteBasket_Generated::DeleteBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-checkout-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_CheckoutBasket_Generated::CheckoutBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithAwsDevEnvironment();

        return builder;
    }
}
