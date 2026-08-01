using BuildingBlocks.Core.Validation;

namespace Challenges.Function.Modules.Progress.Features.SubmitAnswer;

// Contract is deliberately narrow (ADR-0045 §3): challengeId and selectedOption only. Accepting
// points, isCorrect or hintsUsed from the client is NOT ALLOWED — hintsRevealed is read from the
// stored attempt inside the handler, never from this command.
public record SubmitAnswerCommand(
    string OwnerId, string ChallengeId, int SelectedOption) : ICommand<SubmitAnswerResult>;

// SelectedOption is the option the *stored* attempt was graded on, which is not necessarily the one
// this request sent: answering is one-shot (ADR-0045 §4), so a re-submission is answered with the
// first attempt verbatim. Echoing it back makes the response self-describing — a client can render
// the verdict against the option it actually belongs to instead of against whatever the customer
// just clicked.
public record SubmitAnswerResult(
    bool IsCorrect, int PointsEarned, int NewScore, string Explanation, int SelectedOption);

public class SubmitAnswerCommandValidator : IValidator<SubmitAnswerCommand>
{
    public IEnumerable<ValidationFailure> Validate(SubmitAnswerCommand instance)
    {
        if (string.IsNullOrWhiteSpace(instance.OwnerId))
        {
            yield return new(nameof(instance.OwnerId), "OwnerId is required");
        }

        if (string.IsNullOrWhiteSpace(instance.ChallengeId))
        {
            yield return new(nameof(instance.ChallengeId), "ChallengeId is required");
        }

        if (instance.SelectedOption < 0)
        {
            yield return new(nameof(instance.SelectedOption), "SelectedOption must not be negative");
        }
    }
}
