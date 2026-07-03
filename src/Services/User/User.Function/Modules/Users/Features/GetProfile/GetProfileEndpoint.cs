using User.Function.Modules.Users.Features.GetProfile;

namespace User.Function;

// Invoked by the AppSync `myProfile` Lambda resolver with the caller's Cognito claims
// (UserId = sub, Email, Name). See ADR-0017.
public record GetProfileRequest(string UserId, string Email, string Name);

public record GetProfileResponse(
    string UserId,
    string Email,
    string Name,
    string? Phone,
    string? AddressLine,
    string? City,
    string? State,
    string? ZipCode,
    string? Country);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<GetProfileResponse> GetProfile(
        GetProfileRequest request,
        [FromServices] ISender sender)
    {
        var result = await sender.Send(
            new GetProfileCommand(request.UserId, request.Email, request.Name),
            CancellationToken.None);

        return new GetProfileResponse(
            result.UserId, result.Email, result.Name, result.Phone,
            result.AddressLine, result.City, result.State, result.ZipCode, result.Country);
    }
}
