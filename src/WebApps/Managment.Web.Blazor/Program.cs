using Managment.Web.Blazor;
using Managment.Web.Blazor.Auth;
using Managment.Web.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var graphQlEndpoint = new Uri(builder.Configuration["GraphQL:Endpoint"]
    ?? throw new InvalidOperationException("GraphQL:Endpoint is not configured."));
var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled");

if (authEnabled)
{
    // Cognito Hosted UI (PKCE). Authority is the user pool issuer; its OIDC discovery
    // document points authorize/token/logout at the hosted UI domain.
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = builder.Configuration["Auth:Authority"];
        options.ProviderOptions.ClientId = builder.Configuration["Auth:ClientId"];
        options.ProviderOptions.ResponseType = "code";
        options.ProviderOptions.DefaultScopes.Add("email");
        options.ProviderOptions.DefaultScopes.Add("profile");
    });

    // Attaches the Cognito access token as Bearer on every AppSync call — the same
    // token type the React BFF forwards; resolvers read groups from it.
    builder.Services.AddScoped(sp =>
    {
        var handler = new GraphQLAuthorizationMessageHandler(
            sp.GetRequiredService<IAccessTokenProvider>(),
            sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            graphQlEndpoint)
        {
            InnerHandler = new HttpClientHandler(),
        };
        return new GraphQLClient(new HttpClient(handler) { BaseAddress = graphQlEndpoint });
    });
}
else
{
    // Local dev: the SPA's graphql-yoga BFF enforces no Cognito groups, so requests go
    // out unauthenticated and a fake Admin principal keeps [Authorize] markup working.
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationStateProvider, DevAuthenticationStateProvider>();
    builder.Services.AddScoped(_ => new GraphQLClient(new HttpClient { BaseAddress = graphQlEndpoint }));
}

builder.Services.AddScoped<ProductAdminService>();
builder.Services.AddScoped<CampaignAdminService>();
// Image CDN base for building product image URLs from imageIds (ADR-0034).
builder.Services.AddSingleton(new ImageCdn(builder.Configuration["ImageCdn:BaseUrl"] ?? string.Empty));

await builder.Build().RunAsync();
