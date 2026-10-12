namespace E2E.Infrastructure;

// Where and as whom the end-to-end suites run. Nothing here is checked in: the target and the test
// user come from the environment, so the suites skip themselves in CI and on machines that don't
// point at a deployed environment (see tests/E2E/README.md).
public sealed record E2ESettings(Uri BaseUrl, string UserEmail, string UserPassword, bool Headless)
{
    public const string BaseUrlVariable = "E2E_BASE_URL";
    public const string UserEmailVariable = "E2E_USER_EMAIL";
    public const string UserPasswordVariable = "E2E_USER_PASSWORD";

    public static E2ESettings? FromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable);
        var email = Environment.GetEnvironmentVariable(UserEmailVariable);
        var password = Environment.GetEnvironmentVariable(UserPasswordVariable);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        var headless = Environment.GetEnvironmentVariable("E2E_HEADED") is not ("1" or "true");
        return new E2ESettings(new Uri(baseUrl.TrimEnd('/') + "/"), email, password, headless);
    }

    public static bool IsConfigured => FromEnvironment() is not null;
}
