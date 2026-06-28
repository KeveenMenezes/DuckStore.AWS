using System.Diagnostics;

namespace Review.DevelopmentDataSeeder;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Provisioning Review DynamoDB tables", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await scope.ServiceProvider.EnsureReviewTablesCreatedAsync();
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }
}
