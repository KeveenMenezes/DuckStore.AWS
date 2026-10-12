namespace E2E.Infrastructure;

// Where and as whom the end-to-end suites run. Nothing here is checked in: the target and the test
// user come from the environment, so the suites skip themselves in CI and on machines that don't
// point at a deployed environment (see tests/E2E/README.md).
// Headless, SlowMo and TraceDirectory only change how a run can be watched: E2E_HEADED=1 shows the
// browser, E2E_SLOWMO_MS slows every action down, and E2E_TRACE_DIR saves a Playwright trace per test
// class (open it with `npx playwright show-trace <file>`), and E2E_SCREENSHOT_DIR saves a screenshot at
// each moment a test marks with StorefrontSession.CaptureAsync.
public sealed record E2ESettings(
    Uri BaseUrl, string UserEmail, string UserPassword, bool Headless, float SlowMo, string? TraceDirectory,
    string? ScreenshotDirectory)
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
        var slowMo = float.TryParse(Environment.GetEnvironmentVariable("E2E_SLOWMO_MS"), out var ms) ? ms : 0;
        return new E2ESettings(
            new Uri(baseUrl.TrimEnd('/') + "/"), email, password, headless, slowMo,
            OptionalDirectory("E2E_TRACE_DIR"), OptionalDirectory("E2E_SCREENSHOT_DIR"));
    }

    public static bool IsConfigured => FromEnvironment() is not null;

    private static string? OptionalDirectory(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } directory ? directory : null;
}
