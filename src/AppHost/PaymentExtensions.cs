using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Payment;

public static class PaymentExtensions
{
    private const string PaymentsTableName = "payments";

    // PaymentGateway.Function is registered here too rather than in its own extensions file: it
    // owns no DynamoDB table, no seeder, no idempotency inbox (ADR-0025) — a single Lambda
    // registration doesn't warrant a near-empty file of its own.
    public static void AddPaymentServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var paymentMigration = builder.AddProject<Projects.Payment_DevelopmentDataSeeder>("payment-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-basket-checkout-consumer",
                lambdaHandler: "Payment.Function::Payment.Function.Functions_BasketCheckoutConsumer_Generated::BasketCheckoutConsumer")
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-requested-publisher",
                lambdaHandler: "Payment.Function::Payment.Function.Functions_PaymentStreamPublisher_Generated::PaymentStreamPublisher")
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithDynamoDBStreamsEventSource(PaymentsTableName)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-result-authorized-consumer",
                lambdaHandler: "Payment.Function::Payment.Function.Functions_PaymentAuthorizedConsumer_Generated::PaymentAuthorizedConsumer")
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.Payment_Function>(
                "payment-result-declined-consumer",
                lambdaHandler: "Payment.Function::Payment.Function.Functions_PaymentDeclinedConsumer_Generated::PaymentDeclinedConsumer")
            .WaitForCompletion(paymentMigration)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

        builder.AddAWSLambdaFunction<Projects.PaymentGateway_Function>(
                "paymentgateway-consumer",
                lambdaHandler: "PaymentGateway.Function::PaymentGateway.Function.Functions_PaymentRequestedConsumer_Generated::PaymentRequestedConsumer")
            .WithAwsDevEnvironment()
            .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");
    }
}
