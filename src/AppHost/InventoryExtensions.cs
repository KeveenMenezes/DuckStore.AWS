using AppHost.Extensions;
using Aspire.Hosting.AWS.DynamoDB;

namespace AppHost.Inventory;

public static class InventoryExtensions
{
    // Module 1 of SPEC-inventory.md has no Lambda: the warehouse fields are direct resolvers
    // (ADR-0052 §4), served locally by the SPA's local.ts. Only the table and its seed run here.
    public static IResourceBuilder<ProjectResource> AddInventoryServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        return builder.AddProject<Projects.Inventory_DevelopmentDataSeeder>("inventory-data-seeder")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();
    }
}
