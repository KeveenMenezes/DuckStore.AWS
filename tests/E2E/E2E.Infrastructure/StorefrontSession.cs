using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace E2E.Infrastructure;

// One signed-in storefront session per test class: a real browser that logged in through the SPA's
// BFF and Cognito's managed login, exactly like a customer. UI tests drive its page; API tests call
// the SPA's /api/graphql with the same session cookie, so both go through the deployed BFF,
// AppSync and the services behind them — nothing is mocked.
public sealed class StorefrontSession : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public E2ESettings Settings { get; private set; } = null!;
    public IBrowserContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Skipped suites must not download a browser or touch the network.
        if (E2ESettings.FromEnvironment() is not { } settings)
            return;
        Settings = settings;

        // Idempotent: downloads Chromium on the first run only.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = settings.Headless });
        Context = await _browser.NewContextAsync(new() { BaseURL = settings.BaseUrl.ToString() });

        await SignInAsync();
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    public Task<IPage> NewPageAsync() => Context.NewPageAsync();

    // The BFF starts the PKCE flow and Cognito's managed login takes the test user's credentials;
    // the callback leaves the opaque session cookie on the context (ADR-0041).
    private async Task SignInAsync()
    {
        var page = await Context.NewPageAsync();
        await page.GotoAsync("api/auth/login");

        var username = page.Locator("input[name='username'], input[type='email']").First;
        await username.FillAsync(Settings.UserEmail);
        var password = page.Locator("input[type='password']").First;
        // Managed login v2 may ask for the password on a second step.
        if (!await password.IsVisibleAsync())
            await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^(next|continue|sign in)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.ClickAsync();
        await password.FillAsync(Settings.UserPassword);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("^(sign in|continue|log in)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.ClickAsync();

        await page.WaitForURLAsync(url => url.StartsWith(Settings.BaseUrl.ToString(), StringComparison.Ordinal) && !url.Contains("/api/auth/"),
            new() { Timeout = 30_000 });
        await page.CloseAsync();
    }

    // GraphQL through the SPA's BFF with the session cookie — the same path the browser uses.
    public async Task<GraphQLResponse> GraphQLAsync(string query, object? variables = null)
    {
        var response = await Context.APIRequest.PostAsync("api/graphql", new()
        {
            DataObject = new { query, variables },
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
        });
        var body = await response.TextAsync();
        if (!response.Ok)
            throw new InvalidOperationException($"POST /api/graphql returned {response.Status}: {body}");
        return new GraphQLResponse(JsonNode.Parse(body)!.AsObject());
    }
}

public sealed record GraphQLResponse(JsonObject Body)
{
    public JsonNode? Data => Body["data"];
    public JsonArray? Errors => Body["errors"] as JsonArray;
    public bool HasErrors => Errors is { Count: > 0 };

    // AppSync puts errorType/errorInfo on the error itself; the local backend under extensions.
    public string? ErrorType =>
        (string?)(Errors?[0]?["errorType"] ?? Errors?[0]?["extensions"]?["errorType"]);

    public JsonNode? ErrorInfo => Errors?[0]?["errorInfo"] ?? Errors?[0]?["extensions"]?["errorInfo"];

    public override string ToString() => Body.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
