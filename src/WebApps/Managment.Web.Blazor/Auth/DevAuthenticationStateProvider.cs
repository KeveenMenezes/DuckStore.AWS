using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Managment.Web.Blazor.Auth;

/// <summary>
/// Local-dev stand-in for Cognito: always returns an authenticated principal in the
/// Admin group so [Authorize] markup behaves exactly like production. Only registered
/// when Auth:Enabled is false (the local graphql-yoga BFF enforces no groups).
/// </summary>
public sealed class DevAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState State = new(new ClaimsPrincipal(
        new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "dev-admin"),
                new Claim("cognito:groups", "Admin"),
            ],
            authenticationType: "DevAuth")));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(State);
}
