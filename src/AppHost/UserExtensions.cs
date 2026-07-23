using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.User;

public static class UserExtensions
{
    // No Lambda functions — myProfile/updateProfile are AppSync direct DynamoDB resolvers
    // (ADR-0009). This only seeds the user-profiles table for local dev.
    public static IResourceBuilder<ProjectResource> AddUserResources(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var userSeeder = builder.AddProject<Projects.User_DevelopmentDataSeeder>("user-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return userSeeder;
    }
}
