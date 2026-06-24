using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.AWS.DynamoDB;
using AppHost.Extensions;

namespace AppHost.Catalog;

public static class CatalogExtensions
{
    public static IDistributedApplicationBuilder AddCatalogLambdas(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DynamoDBLocalResource> dynamoDb)
    {
        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-create-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_CreateProduct_Generated::CreateProduct")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-get-products",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_GetProducts_Generated::GetProducts")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-get-product-by-id",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_GetProductById_Generated::GetProductById")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-update-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_UpdateProduct_Generated::UpdateProduct")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-delete-product",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_DeleteProduct_Generated::DeleteProduct")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-get-product-by-category",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_GetProductByCategory_Generated::GetProductByCategory")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        builder.AddAWSLambdaFunction<Projects.Catalog_Function>(
                "catalog-get-categories",
                lambdaHandler: "Catalog.Function::Catalog.Function.Functions_GetCategories_Generated::GetCategories")
            .WaitFor(dynamoDb)
            .WithReference(dynamoDb)
            .WithAwsDevEnvironment();

        return builder;
    }
}
