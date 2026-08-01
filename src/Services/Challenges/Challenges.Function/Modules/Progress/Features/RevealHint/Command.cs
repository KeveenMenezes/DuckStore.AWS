using BuildingBlocks.Core.Validation;

namespace Challenges.Function.Modules.Progress.Features.RevealHint;

public record RevealHintCommand(string OwnerId, string ChallengeId) : ICommand<RevealHintResult>;

public record RevealHintResult(string Hint, int HintsRevealed, int PenaltyApplied);

public class RevealHintCommandValidator : IValidator<RevealHintCommand>
{
    public IEnumerable<ValidationFailure> Validate(RevealHintCommand instance)
    {
        if (string.IsNullOrWhiteSpace(instance.OwnerId))
        {
            yield return new(nameof(instance.OwnerId), "OwnerId is required");
        }

        if (string.IsNullOrWhiteSpace(instance.ChallengeId))
        {
            yield return new(nameof(instance.ChallengeId), "ChallengeId is required");
        }
    }
}
