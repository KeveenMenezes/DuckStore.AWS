using System.Diagnostics;

namespace CatalogView.DevelopmentDataSeeder;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Provisioning CatalogView OpenSearch index and backfill", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            var backfill = scope.ServiceProvider.GetRequiredService<ProductBackfill>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }
}
