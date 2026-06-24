using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.AWS.DynamoDB;
using AppHost.Extensions;

namespace AppHost.Discount;

public static class DiscountExtensions
{
    public static IResourceBuilder<ProjectResource> AddDiscountApi(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb,
        IResourceBuilder<ElasticsearchResource> elasticsearch) =>
        builder.AddProject<Projects.Discount_Grpc>("discount-api", "http")
            .WaitFor(dynamoDb)
            .WaitFor(elasticsearch)
            .WithReference(dynamoDb)
            .WithReference(elasticsearch)
            .WithAwsDevEnvironment();
}
