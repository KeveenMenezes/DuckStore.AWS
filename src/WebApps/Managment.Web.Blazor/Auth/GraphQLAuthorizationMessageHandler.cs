using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Managment.Web.Blazor.Auth;

/// <summary>
/// Attaches the Cognito access token (Bearer) to requests targeting the AppSync
/// GraphQL endpoint. AppSync resolvers read ctx.identity.groups from this token
/// to enforce the Admin/Seller group checks.
/// </summary>
public sealed class GraphQLAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public GraphQLAuthorizationMessageHandler(
        IAccessTokenProvider provider,
        NavigationManager navigation,
        Uri graphQlEndpoint)
        : base(provider, navigation)
    {
        ConfigureHandler(authorizedUrls: [graphQlEndpoint.GetLeftPart(UriPartial.Authority)]);
    }
}
