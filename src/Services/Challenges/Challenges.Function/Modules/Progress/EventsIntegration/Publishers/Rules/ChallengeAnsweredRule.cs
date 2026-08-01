using Challenges.Function.Modules.Progress.Data;

namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers.Rules;

// Fires exactly once per attempt: the transition of an ATTEMPT# row from "not yet answered"
// (IsCorrect absent) to "answered" (IsCorrect now set) — ADR-0045 §9. That transition can arrive
// as either an INSERT (answered directly, no prior hint) or a MODIFY (a hint was revealed first,
// so the row already existed as a hint-only placeholder), so both event types are matched; what
// actually gates publication is the IsCorrect transition itself, never the DynamoDB event name.
// A plain hint reveal (HintsRevealed changing, IsCorrect still absent on both sides) never
// matches, and neither does any write to PROFILE (its SK never starts with ATTEMPT#).
public sealed class ChallengeAnsweredRule : IStreamRule<ProgressStreamImage>
{
    public bool Match(StreamContext<ProgressStreamImage> context) =>
        (context.New?.SK.StartsWith(ProgressSchema.AttemptSortKeyPrefix, StringComparison.Ordinal) ?? false)
        && context.New.IsCorrect.HasValue
        && context.Old?.IsCorrect is null;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ProgressStreamImage> context, CancellationToken cancellationToken = default)
    {
        var attempt = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(ChallengeAnsweredEvent),
            new ChallengeAnsweredEvent
            {
                OwnerId = attempt.OwnerId,
                QuestionId = ProgressSchema.ParseAttemptQuestionId(attempt.SK),
                IsCorrect = attempt.IsCorrect!.Value,
                SelectedOption = attempt.SelectedOption,
                HintsRevealed = attempt.HintsRevealed,
                PointsEarned = attempt.PointsEarned
            }));
    }
}
