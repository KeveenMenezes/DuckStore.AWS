using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;
using Aspire.Hosting.AWS.Lambda;

namespace AppHost.User;

public static class UserExtensions
{
    public static IResourceBuilder<LambdaProjectResource> AddUserLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        var userSeeder = builder.AddProject<Projects.User_DevelopmentDataSeeder>("user-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        // Invoked by the AppSync `myProfile` Lambda resolver (lazy provisioning — ADR-0017).
        var getProfile = builder.AddAWSLambdaFunction<Projects.User_Function>(
                "user-get-profile",
                lambdaHandler: "User.Function::User.Function.Functions_GetProfile_Generated::GetProfile")
            .WaitForCompletion(userSeeder)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return getProfile;
    }
}
