var builder = DistributedApplication.CreateBuilder(args);

//Create DataBase 
var catalogDb = builder.AddPostgres("catalogDb");

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalogapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(catalogDb)
    .WithReference(catalogDb)
    .WithHttpsHealthCheck("/health");

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basketapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints();

builder.AddNpmApp("shopping", "../DuckStore.WebApp.ANG")
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


