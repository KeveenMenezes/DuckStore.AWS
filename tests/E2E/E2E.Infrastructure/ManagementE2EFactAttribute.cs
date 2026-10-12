namespace E2E.Infrastructure;

// A [Fact] for the management-app suites: skips itself unless the environment names the management
// app and its three staff test users (and, with requiresStorefront, the storefront customer too), so
// `dotnet test DuckStore.slnx` never reaches a deployed environment by accident.
public sealed class ManagementE2EFactAttribute : FactAttribute
{
    public ManagementE2EFactAttribute(bool requiresStorefront = false)
    {
        if (!ManagementSettings.IsConfigured)
            Skip = $"End-to-end: set {ManagementSettings.RequiredVariables} to run (see tests/E2E/README.md).";
        else if (requiresStorefront && !E2ESettings.IsConfigured)
            Skip = $"End-to-end: also set {E2ESettings.BaseUrlVariable}, {E2ESettings.UserEmailVariable} and " +
                   $"{E2ESettings.UserPasswordVariable} for the storefront customer (see tests/E2E/README.md).";
    }
}
