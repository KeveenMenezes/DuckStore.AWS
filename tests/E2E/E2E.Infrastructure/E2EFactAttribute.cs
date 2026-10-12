namespace E2E.Infrastructure;

// A [Fact] that skips itself unless the environment names a target and a test user, so
// `dotnet test DuckStore.slnx` (CI included) never reaches out to a deployed environment by accident.
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!E2ESettings.IsConfigured)
        {
            Skip = $"End-to-end: set {E2ESettings.BaseUrlVariable}, {E2ESettings.UserEmailVariable} and " +
                   $"{E2ESettings.UserPasswordVariable} to run (see tests/E2E/README.md).";
        }
    }
}
