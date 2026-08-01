using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using static AppHost.Extensions.Extensions;

namespace AppHost.Pricing;

public static class PricingExtensions
{
    // nominalPriceFor/currentDiscountForProduct are AppSync direct DynamoDB resolvers (ADR-0009),
    // not Lambdas — so Pricing only registers its Lambda-backed mutations/queries and its
    // ProductDeletedEvent cleanup consumer here.
    public static IResourceBuilder<ProjectResource> AddPricingServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var pricingMigration = builder.AddProject<Projects.Pricing_DevelopmentDataSeeder>("pricing-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // setNominalPrice is gone — it's now an AppSync direct DynamoDB UpdateItem resolver (ADR-0009).

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-get-installment-plan",
                lambdaHandler: LambdaHandler("Pricing.Function", "GetInstallmentPlan"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("Installments__ActiveProvider", "Simulated")
            .WithEnvironment("Installments__MinMarginPercent", "5")
            .WithEnvironment("Installments__ValueTiers__0__MinAmount", "150")
            .WithEnvironment("Installments__ValueTiers__0__MaxInstallments", "6")
            .WithEnvironment("Installments__ValueTiers__1__MinAmount", "300")
            .WithEnvironment("Installments__ValueTiers__1__MaxInstallments", "10");

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-get-basket-installment-plan",
                lambdaHandler:
                LambdaHandler("Pricing.Function", "GetBasketInstallmentPlan"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("Installments__ActiveProvider", "Simulated")
            .WithEnvironment("Installments__MinMarginPercent", "5")
            .WithEnvironment("Installments__ValueTiers__0__MinAmount", "150")
            .WithEnvironment("Installments__ValueTiers__0__MaxInstallments", "6")
            .WithEnvironment("Installments__ValueTiers__1__MinAmount", "300")
            .WithEnvironment("Installments__ValueTiers__1__MaxInstallments", "10");

        // setGatewayCost is gone — it's now an AppSync direct DynamoDB UpdateItem resolver (ADR-0009).

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-create-campaign",
                lambdaHandler: LambdaHandler("Pricing.Function", "CreateCampaign"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-end-campaign",
                lambdaHandler: LambdaHandler("Pricing.Function", "EndCampaign"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-product-deleted-consumer",
                lambdaHandler:
                LambdaHandler("Pricing.Function", "ProductDeletedConsumer"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-points-redeemed-consumer",
                lambdaHandler:
                LambdaHandler("Pricing.Function", "PointsRedeemedConsumer"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment("Rewards__PointsPerUnit", "100")
            .WithEnvironment("Rewards__CurrencyPerUnit", "10")
            .WithEnvironment("Rewards__ExpiryDays", "90");

        // Consumes PaymentAuthorizedEvent (published by PaymentGateway.Function — same event
        // Ordering's own consumer reacts to) and burns the customer discount used at checkout, if
        // any (ADR-0046 §6). Nothing reacts to PaymentDeclinedEvent — a declined payment leaves the
        // discount Issued and reusable.
        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-payment-authorized-consumer",
                lambdaHandler:
                LambdaHandler("Pricing.Function", "PaymentAuthorizedConsumer"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // CDC publisher: prices INSERT/MODIFY → DynamoDB Stream → publish a single PriceChangedEvent
        // (nominal price + payment badge) to EventBridge so CatalogView syncs both in one merge
        // (ADR-0026/0027/0028). Needs the same installment settings as pricing-get-installment-plan
        // since it computes the badge using the active GatewayCost.
        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-prices-stream-publisher",
                lambdaHandler: LambdaHandler("Pricing.Function", "PriceStreamPublisher"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource("prices")
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment("Installments__ActiveProvider", "Simulated")
            .WithEnvironment("Installments__MinMarginPercent", "5")
            .WithEnvironment("Installments__ValueTiers__0__MinAmount", "150")
            .WithEnvironment("Installments__ValueTiers__0__MaxInstallments", "6")
            .WithEnvironment("Installments__ValueTiers__1__MinAmount", "300")
            .WithEnvironment("Installments__ValueTiers__1__MaxInstallments", "10");

        // CDC publisher: product-discounts INSERT/REMOVE → DynamoDB Stream → publish
        // ProductDiscountChangedEvent so CatalogView refreshes the catalog price when a campaign
        // starts, ends or expires. Campaigns never write to `prices`, so without this trigger the
        // denormalized price would never reflect a campaign at all (ADR-0044).
        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-product-discounts-stream-publisher",
                lambdaHandler: LambdaHandler("Pricing.Function", "ProductDiscountStreamPublisher"))
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource("product-discounts")
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
            .WithEnvironment("Installments__ActiveProvider", "Simulated")
            .WithEnvironment("Installments__MinMarginPercent", "5")
            .WithEnvironment("Installments__ValueTiers__0__MinAmount", "150")
            .WithEnvironment("Installments__ValueTiers__0__MaxInstallments", "6")
            .WithEnvironment("Installments__ValueTiers__1__MinAmount", "300")
            .WithEnvironment("Installments__ValueTiers__1__MaxInstallments", "10");

        return pricingMigration;
    }
}
