using System.Diagnostics;
using Challenges.Function.Modules.Questions.Data;

namespace Challenges.DevelopmentDataSeeder;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Provisioning Challenges DynamoDB tables", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await scope.ServiceProvider.EnsureChallengesTablesCreatedAsync();

            var questionRepository = scope.ServiceProvider.GetRequiredService<IQuestionRepository>();
            await SeedQuestionsAsync(questionRepository);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task SeedQuestionsAsync(IQuestionRepository questionRepository)
    {
        if (await questionRepository.AnyAsync())
            return;

        foreach (var question in ChallengesInitialData.Questions)
            await questionRepository.AddAsync(question);
    }
}
