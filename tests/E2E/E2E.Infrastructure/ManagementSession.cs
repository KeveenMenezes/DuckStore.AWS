using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace E2E.Infrastructure;

// One signed-in management-app session per test class and staff group: a real browser that logged in
// to the Blazor app through the management pool's managed login, exactly like staff do. UI tests drive
// its Page; API tests call AppSync directly with the same access token the app attaches, so both reach
// the deployed resolvers — nothing is mocked.
//
// The Blazor OIDC library keeps the token in the tab's sessionStorage, so the session keeps the tab
// it signed in with (Page) and tests navigate inside it rather than opening new pages.
public abstract class ManagementSession : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IAPIRequestContext? _api;
    private bool _tracing;

    public ManagementSettings Settings { get; private set; } = null!;
    public ManagementUser User { get; private set; } = null!;
    public IBrowserContext Context { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;
    public Uri GraphQLEndpoint { get; private set; } = null!;

    protected abstract ManagementUser UserFrom(ManagementSettings settings);

    public async Task InitializeAsync()
    {
        // Skipped suites must not download a browser or touch the network.
        if (ManagementSettings.FromEnvironment() is not { } settings)
            return;
        Settings = settings;
        User = UserFrom(settings);

        // Idempotent: downloads Chromium on the first run only.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = settings.Headless, SlowMo = settings.SlowMo });
        Context = await _browser.NewContextAsync(new()
        {
            BaseURL = settings.ManagementUrl.ToString(),
            ViewportSize = new() { Width = 1366, Height = 900 },
        });
        // API calls go through their own request context, outside the browser context's trace, so the
        // bearer tokens they carry are not recorded (the app's own AppSync calls still are — see
        // tests/E2E/README.md on handling trace files).
        _api = await _playwright.APIRequest.NewContextAsync();
        GraphQLEndpoint = await ReadGraphQLEndpointAsync();
        await SignInAsync();

        // Started after sign-in on purpose: a trace records the values typed into fields, and the
        // test user's password must never end up in a trace file.
        if (settings.TraceDirectory is not null)
        {
            await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });
            _tracing = true;
        }
    }

    public async Task DisposeAsync()
    {
        // Only when tracing actually started: a sign-in failure must surface as itself.
        if (_tracing && Settings.TraceDirectory is { } directory)
        {
            Directory.CreateDirectory(directory);
            await Context.Tracing.StopAsync(new()
            {
                Path = Path.Combine(directory, $"management-{User.Group}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip"),
            });
        }

        if (_api is not null)
            await _api.DisposeAsync();
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    // A named screenshot of a moment worth seeing, saved only when E2E_SCREENSHOT_DIR is set.
    public async Task CaptureAsync(string moment)
    {
        if (Settings.ScreenshotDirectory is not { } directory)
            return;
        Directory.CreateDirectory(directory);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{User.Group}-{moment}.png"), FullPage = true });
    }

    // Navigates inside the signed-in tab and waits for the app to finish booting and authorizing.
    public async Task GotoAsync(string path)
    {
        await Page.GotoAsync(path);
        await Page.Locator(".sidebar-nav").WaitForAsync();
        await Page.GetByText("Checking your session...").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    // GraphQL straight to AppSync with the app's own access token — the call the Blazor app makes.
    public async Task<GraphQLResponse> GraphQLAsync(string query, object? variables = null)
    {
        var token = await Page.EvaluateAsync<string?>(
            """
            () => {
              const key = Object.keys(sessionStorage).find(k => k.startsWith('oidc.user:'));
              return key ? JSON.parse(sessionStorage.getItem(key)).access_token : null;
            }
            """) ?? throw new InvalidOperationException("No access token in the management app's session storage.");

        var response = await _api!.PostAsync(GraphQLEndpoint.ToString(), new()
        {
            DataObject = new { query, variables },
            Headers = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["authorization"] = $"Bearer {token}",
            },
        });
        var body = await response.TextAsync();
        // AppSync answers 401 for some auth failures and 200 with errors for resolver ones (util.unauthorized).
        if (!response.Ok && response.Status != 401)
            throw new InvalidOperationException($"POST {GraphQLEndpoint} returned {response.Status}: {body}");
        return new GraphQLResponse(JsonNode.Parse(body)!.AsObject());
    }

    // The deployed app's own runtime config (the deploy workflow bakes the AppSync endpoint into it).
    private async Task<Uri> ReadGraphQLEndpointAsync()
    {
        var response = await _api!.GetAsync(new Uri(Settings.ManagementUrl, "appsettings.json").ToString());
        var config = JsonNode.Parse(await response.TextAsync())!;
        return new Uri((string?)config["GraphQL"]?["Endpoint"]
            ?? throw new InvalidOperationException("appsettings.json has no GraphQL:Endpoint."));
    }

    // The app's RedirectToLogin starts the PKCE flow and Cognito's managed login takes the test
    // user's credentials; the callback leaves the tokens in this tab's sessionStorage.
    private async Task SignInAsync()
    {
        Page = await Context.NewPageAsync();
        await Page.GotoAsync("authentication/login");

        var username = Page.Locator("input[name='username'], input[type='email']").First;
        await username.FillAsync(User.Email);
        var password = Page.Locator("input[type='password']").First;
        // Managed login v2 may ask for the password on a second step.
        if (!await password.IsVisibleAsync())
            await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^(next|continue|sign in)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.ClickAsync();
        await password.FillAsync(User.Password);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^(sign in|continue|log in)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.ClickAsync();

        await Page.WaitForURLAsync(url => url.StartsWith(Settings.ManagementUrl.ToString(), StringComparison.Ordinal) && !url.Contains("/authentication/"),
            new() { Timeout = 30_000 });
        await Page.Locator(".user-name").WaitForAsync();
    }
}

public sealed class StockKeeperSession : ManagementSession
{
    protected override ManagementUser UserFrom(ManagementSettings settings) => settings.StockKeeper;
}

public sealed class SellerSession : ManagementSession
{
    protected override ManagementUser UserFrom(ManagementSettings settings) => settings.Seller;
}

public sealed class AdminSession : ManagementSession
{
    protected override ManagementUser UserFrom(ManagementSettings settings) => settings.Admin;
}
