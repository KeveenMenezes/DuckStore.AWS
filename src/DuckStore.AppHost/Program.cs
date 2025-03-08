var builder = DistributedApplication.CreateBuilder(args);

//Create DataBase 
var catalogDb = builder.AddPostgres("catalogDb");

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalogapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(catalogDb);

builder.AddNpmApp("angularapi", "../DuckStore.WebApps.AG")
    .WithReference(catalogApi)
    .WaitFor(catalogApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() =>
    // Attempt to parse the environment variable value; return true if it's exactly "1".
    int.TryParse(
        Environment.GetEnvironmentVariable("ESHOP_USE_HTTP_ENDPOINTS"),
        out int result) && result == 1 ? "http" : "https";
