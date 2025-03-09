var builder = DistributedApplication.CreateBuilder(args);

//Create DataBase 
var catalogDb = builder.AddPostgres("catalogDb");

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalog-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(catalogDb)
    .WithReference(catalogDb);

builder.AddNpmApp("shopping-view", "../DuckStore.WebApp.ANG")
    .WithReference(catalogApi)
    .WaitFor(catalogApi)
    .WithHttpsEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() =>
    // Attempt to parse the environment variable value; return true if it's exactly "1".
    int.TryParse(
        Environment.GetEnvironmentVariable("ESHOP_USE_HTTP_ENDPOINTS"),
        out int result) && result == 1 ? "http" : "https";
