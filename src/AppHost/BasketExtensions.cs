using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;
using AppHost.Discount;
using AppHost.Extensions;

namespace AppHost.Basket;

public record BasketResources(
    IResourceBuilder<LambdaProjectResource> GetBasket,
    IResourceBuilder<LambdaProjectResource> StoreBasket,
    IResourceBuilder<LambdaProjectResource> DeleteBasket,
    IResourceBuilder<LambdaProjectResource> CheckoutBasket
);

public static class BasketExtensions
{
    private const string ShoppingCartsTableName = "ShoppingCarts";

    public static BasketResources AddBasketLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<RedisResource> redis,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<LambdaEmulatorResource> lambdaEmulator)
    {
        var getBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-get-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_GetBasket_Generated::GetBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithAwsDevEnvironment();

        var storeBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-store-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_StoreBasket_Generated::StoreBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithLambdaInvokeTarget(lambdaEmulator, DiscountExtensions.GetDiscountFunctionName)
            .WithAwsDevEnvironment();

        var deleteBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-delete-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_DeleteBasket_Generated::DeleteBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithAwsDevEnvironment();

        var checkoutBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-checkout-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_CheckoutBasket_Generated::CheckoutBasket")
            .WaitFor(dynamoDb)
            .WaitFor(redis)
            .WithReference(dynamoDb)
            .WithReference(redis)
            .WithAwsDevEnvironment();

        return new BasketResources(getBasket, storeBasket, deleteBasket, checkoutBasket);
    }
}
