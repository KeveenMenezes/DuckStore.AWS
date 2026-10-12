namespace E2E.Infrastructure;

// Where and as whom the management-app suites run: the deployed Blazor app and one test user per
// staff group. Nothing here is checked in — the values come from the environment
// (~/.config/duckstore/e2e-inventory.env on a dev machine), so the suites skip themselves anywhere
// that doesn't set them (see tests/E2E/README.md). The watch options are the storefront's
// (E2E_HEADED, E2E_SLOWMO_MS, E2E_TRACE_DIR, E2E_SCREENSHOT_DIR).
public sealed record ManagementUser(string Group, string Email, string Password);

public sealed record ManagementSettings(
    Uri ManagementUrl, ManagementUser StockKeeper, ManagementUser Seller, ManagementUser Admin,
    bool Headless, float SlowMo, string? TraceDirectory, string? ScreenshotDirectory)
{
    public const string ManagementUrlVariable = "E2E_MANAGEMENT_URL";

    private static readonly string[] Groups = ["STOCKKEEPER", "SELLER", "ADMIN"];

    public static string RequiredVariables =>
        $"{ManagementUrlVariable} and E2E_{{{string.Join(",", Groups)}}}_{{EMAIL,PASSWORD}}";

    public static ManagementSettings? FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable(ManagementUrlVariable);
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var users = Groups.Select(UserFromEnvironment).ToArray();
        if (users.Any(user => user is null))
            return null;

        var headless = Environment.GetEnvironmentVariable("E2E_HEADED") is not ("1" or "true");
        var slowMo = float.TryParse(Environment.GetEnvironmentVariable("E2E_SLOWMO_MS"), out var ms) ? ms : 0;
        return new ManagementSettings(
            new Uri(url.TrimEnd('/') + "/"), users[0]!, users[1]!, users[2]!, headless, slowMo,
            OptionalDirectory("E2E_TRACE_DIR"), OptionalDirectory("E2E_SCREENSHOT_DIR"));
    }

    public static bool IsConfigured => FromEnvironment() is not null;

    // E2E_STOCKKEEPER_EMAIL/E2E_STOCKKEEPER_PASSWORD → the StockKeeper group's test user, and so on.
    private static ManagementUser? UserFromEnvironment(string group)
    {
        var email = Environment.GetEnvironmentVariable($"E2E_{group}_EMAIL");
        var password = Environment.GetEnvironmentVariable($"E2E_{group}_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;
        var name = group switch { "STOCKKEEPER" => "StockKeeper", "SELLER" => "Seller", _ => "Admin" };
        return new ManagementUser(name, email, password);
    }

    private static string? OptionalDirectory(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } directory ? directory : null;
}
