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

        var question = await questionRepository.GetForGradingAsync(questionId, cancellationToken)
            ?? throw new QuestionNotFoundException(command.ChallengeId);

        // hintsRevealed always comes from the stored attempt, never from the client (ADR-0045 §3).
        var hintsRevealed = await progressRepository.GetHintsRevealedAsync(ownerId, questionId, cancellationToken);
        var attemptResult = question.Grade(command.SelectedOption, hintsRevealed);

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
