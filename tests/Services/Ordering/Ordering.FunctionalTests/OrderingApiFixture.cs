using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace Ordering.FunctionalTests;

public sealed class OrderingApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;

    public Task InitializeAsync()
    {
        throw new NotImplementedException();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        throw new NotImplementedException();
    }
}
