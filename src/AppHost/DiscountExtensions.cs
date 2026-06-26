using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;
using AppHost.Extensions;

namespace AppHost.Discount;

public static class DiscountExtensions
{
    public const string GetDiscountFunctionName = "discount-get-discount";

    public static IResourceBuilder<LambdaProjectResource> AddDiscountLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb) =>
        builder.AddAWSLambdaFunction<Projects.Discount_Function>(
                GetDiscountFunctionName,
                lambdaHandler: "Discount.Function::Discount.Function.Functions_GetDiscount_Generated::GetDiscount")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();
}
