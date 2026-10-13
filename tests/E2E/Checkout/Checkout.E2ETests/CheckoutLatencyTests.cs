using System.Diagnostics;
using Xunit.Abstractions;

namespace Checkout.E2ETests;

// Latency of checkoutBasket as the client sees it, through the BFF and AppSync. The baseline was
// taken against the old pipeline before the saga shipped: p50 590 ms, p95 830 ms. Run alone with --filter Category=Latency; the figures go to the
// test output.
[Trait("Category", "Latency")]
public sealed class CheckoutLatencyTests(StorefrontSession session, ITestOutputHelper output)
    : IClassFixture<StorefrontSession>
{
    private const int WarmUp = 2;
    private const int Samples = 10;

    [E2EFact]
    public async Task CheckoutBasket_Latency()
    {
        var storefront = new Storefront(session);
        var product = (await storefront.ProductsAsync(1))[0];
        var lines = new List<Storefront.CartLine> { new(product, 1) };
        var timings = new List<double>();

        for (var i = 0; i < WarmUp + Samples; i++)
        {
            await storefront.StoreCartAsync(lines);
            var input = Storefront.CheckoutInput(await storefront.QuoteAsync(lines), Guid.NewGuid().ToString());

            var watch = Stopwatch.StartNew();
            var response = await storefront.CheckoutAsync(input);
            watch.Stop();

            Storefront.OrderIdOf(response);
            if (i >= WarmUp)
                timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        double Percentile(double p) => timings[(int)Math.Ceiling(p * timings.Count) - 1];
        output.WriteLine($"checkoutBasket over {Samples} warm calls: p50 = {Percentile(0.5):F0} ms, " +
                         $"p95 = {Percentile(0.95):F0} ms, min = {timings[0]:F0} ms, max = {timings[^1]:F0} ms");
    }
}
