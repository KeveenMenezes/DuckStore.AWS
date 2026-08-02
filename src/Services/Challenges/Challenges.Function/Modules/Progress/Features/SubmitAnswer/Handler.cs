namespace Challenges.Function.Modules.Progress.Features.SubmitAnswer;

// Thin by design (thin-handlers-rich-domain): load, delegate the grading and scoring to the
// domain, save, done. All the arithmetic — the hint penalty, the score/streak/KPI bookkeeping —
// lives in Question.Grade and PlayerProgress.Apply, never here.
public class SubmitAnswerHandler(
    IQuestionRepository questionRepository, IPlayerProgressRepository progressRepository)
    : ICommandHandler<SubmitAnswerCommand, SubmitAnswerResult>
{
    public async ValueTask<SubmitAnswerResult> Handle(
        SubmitAnswerCommand command, CancellationToken cancellationToken)
    {
        var ownerId = OwnerId.Of(command.OwnerId);
        var questionId = QuestionId.Of(command.ChallengeId);

        // Two different tables, neither keyed off the other's result — grading needs both, so they
        // are read together rather than one after the other (this Lambda is AppSync-synchronous:
        // the player waits through every round trip it makes).
        var questionTask = questionRepository.GetForGradingAsync(questionId, cancellationToken);
        // hintsRevealed always comes from the stored attempt, never from the client (ADR-0045 §3).
        var hintsRevealedTask = progressRepository.GetHintsRevealedAsync(ownerId, questionId, cancellationToken);

        await Task.WhenAll(questionTask, hintsRevealedTask);

        var question = await questionTask
            ?? throw new QuestionNotFoundException(command.ChallengeId);
        var attemptResult = question.Grade(command.SelectedOption, await hintsRevealedTask);

        var delta = PlayerProgress.CreateEmpty(ownerId);
        delta.Apply(attemptResult, question.Language.Value);

        var attempt = await progressRepository.SaveAttemptAsync(ownerId, delta, cancellationToken);
        var newScore = await progressRepository.GetScoreAsync(ownerId, cancellationToken);

        // Every field comes off `attempt`, never off `command`/`attemptResult`: on a re-submission
        // SaveAttemptAsync returns the attempt already on record, and the response has to describe
        // that one (ADR-0045 §4).
        return new SubmitAnswerResult(
            attempt.IsCorrect, attempt.PointsEarned, newScore, question.Explanation, attempt.SelectedOption);
    }
}
