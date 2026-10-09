using System.Security.Claims;
using System.Text.Json;

namespace Managment.Web.Blazor.Auth;

/// <summary>
/// Reads Cognito group membership off the signed-in principal, for hiding actions the
/// AppSync resolvers would reject anyway (they stay the authority). Cognito sends
/// "cognito:groups" as a JSON array; depending on how the OIDC principal factory maps it,
/// a claim holds either one group or the raw array JSON, and DevAuthenticationStateProvider
/// issues a plain "Admin" — both shapes are accepted.
/// </summary>
public static class CognitoGroups
{
    public const string ClaimType = "cognito:groups";
    public const string Admin = "Admin";

    public static bool Contains(ClaimsPrincipal user, string group) =>
        user.FindAll(ClaimType).Any(claim => ClaimHolds(claim.Value, group));

    private static bool ClaimHolds(string value, string group)
    {
        if (!value.StartsWith('['))
            return value == group;

        try
        {
            using var groups = JsonDocument.Parse(value);
            return groups.RootElement.EnumerateArray().Any(element => element.GetString() == group);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // A group name that merely starts with '[' — compare it as a single group.
            return value == group;
        }
    }
}
