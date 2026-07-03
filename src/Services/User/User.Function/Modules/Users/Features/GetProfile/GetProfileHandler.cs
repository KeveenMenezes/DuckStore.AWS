namespace User.Function.Modules.Users.Features.GetProfile;

// Get-or-create: returns the profile, provisioning it from the Cognito claims on first access
// (lazy provisioning, ADR-0017). Modeled as a command because it may write.
public record GetProfileCommand(string UserId, string Email, string Name) : ICommand<UserProfileDto>;

public class GetProfileCommandValidator : AbstractValidator<GetProfileCommand>
{
    public GetProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required");
    }
}

public class GetProfileCommandHandler(IUserProfileRepository repository)
    : ICommandHandler<GetProfileCommand, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(GetProfileCommand command, CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(command.UserId, cancellationToken);

        if (profile is null)
        {
            profile = UserProfile.Create(command.UserId, command.Email, command.Name);
            await repository.PutAsync(profile, cancellationToken);
        }

        return ToDto(profile);
    }

    private static UserProfileDto ToDto(UserProfile p) =>
        new(p.UserId, p.Email, p.Name, p.Phone, p.AddressLine, p.City, p.State, p.ZipCode, p.Country);
}
