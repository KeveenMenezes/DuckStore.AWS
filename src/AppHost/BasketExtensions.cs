using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;

namespace AppHost.Basket;

public record BasketResources(
    IResourceBuilder<LambdaProjectResource> StoreBasket,
    IResourceBuilder<LambdaProjectResource> CheckoutBasket,
    IResourceBuilder<LambdaProjectResource> MergeBasket
);

public static class BasketExtensions
{
    private const string ShoppingCartsTableName = "shopping-carts";

    public static BasketResources AddBasketLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var basketSeeder = builder.AddProject<Projects.Basket_DevelopmentDataSeeder>("basket-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-shopping-carts-event-publisher",
                lambdaHandler: "Basket.Function::Basket.Function.EventsIntegration.Publisher.ShoppingCartsEventPublisherFunction::FunctionHandler")
            .WaitForCompletion(basketSeeder)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(ShoppingCartsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        var storeBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-store-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_StoreBasket_Generated::StoreBasket")
            .WaitForCompletion(basketSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        var checkoutBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-checkout-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_CheckoutBasket_Generated::CheckoutBasket")
            .WaitForCompletion(basketSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        var mergeBasket = builder.AddAWSLambdaFunction<Projects.Basket_Function>(
                "basket-merge-basket",
                lambdaHandler: "Basket.Function::Basket.Function.Functions_MergeBasket_Generated::MergeBasket")
            .WaitForCompletion(basketSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return new BasketResources(storeBasket, checkoutBasket, mergeBasket);
    }
}
