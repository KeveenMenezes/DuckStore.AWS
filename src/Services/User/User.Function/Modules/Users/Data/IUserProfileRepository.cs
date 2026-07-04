namespace User.Function.Modules.Users.Data;

public interface IUserProfileRepository
{
    // Returns null when the profile has not been provisioned yet (lazy provisioning).
    Task<UserProfile?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task PutAsync(UserProfile profile, CancellationToken cancellationToken = default);
}
