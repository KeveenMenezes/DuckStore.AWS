using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;
using AppHost.Extensions;

namespace AppHost.Discount;

public static class DiscountExtensions
{
    public const string GetDiscountFunctionName = "discount-get-discount";

    public static IResourceBuilder<LambdaProjectResource> AddDiscountLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var discountSeeder = builder.AddProject<Projects.Discount_DevelopmentDataSeeder>("discount-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return builder.AddAWSLambdaFunction<Projects.Discount_Function>(
                GetDiscountFunctionName,
                lambdaHandler: "Discount.Function::Discount.Function.Functions_GetDiscount_Generated::GetDiscount")
            .WaitForCompletion(discountSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();
    }
}
