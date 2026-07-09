using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Pricing;

public static class PricingExtensions
{
    // nominalPriceFor/currentDiscountForProduct are AppSync direct DynamoDB resolvers (ADR-0009),
    // not Lambdas — so Pricing only registers its Lambda-backed mutations/queries and its
    // CatalogUpdatedEvent cleanup consumer here.
    public static IResourceBuilder<ProjectResource> AddPricingServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var pricingMigration = builder.AddProject<Projects.Pricing_DevelopmentDataSeeder>("pricing-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-set-nominal-price",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_SetNominalPrice_Generated::SetNominalPrice")
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-get-installment-plan",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_GetInstallmentPlan_Generated::GetInstallmentPlan")
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
                "Pricing.Function::Pricing.Function.Functions_GetBasketInstallmentPlan_Generated::GetBasketInstallmentPlan")
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
                "pricing-set-gateway-cost",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_SetGatewayCost_Generated::SetGatewayCost")
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-create-campaign",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_CreateCampaign_Generated::CreateCampaign")
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-end-campaign",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_EndCampaign_Generated::EndCampaign")
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-catalog-product-removed-consumer",
                lambdaHandler:
                "Pricing.Function::Pricing.Function.Functions_CatalogProductRemovedConsumer_Generated::CatalogProductRemovedConsumer")
            .WaitForCompletion(pricingMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        // CDC publisher: prices INSERT/MODIFY → DynamoDB Stream → publish a single PriceChangedEvent
        // (nominal price + payment badge) to EventBridge so CatalogView syncs both in one merge
        // (ADR-0026/0027/0028). Needs the same installment settings as pricing-get-installment-plan
        // since it computes the badge using the active GatewayCost.
        builder.AddAWSLambdaFunction<Projects.Pricing_Function>(
                "pricing-prices-event-publisher",
                lambdaHandler: "Pricing.Function::Pricing.Function.Functions_PriceStreamPublisher_Generated::PriceStreamPublisher")
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

        return pricingMigration;
    }
}
