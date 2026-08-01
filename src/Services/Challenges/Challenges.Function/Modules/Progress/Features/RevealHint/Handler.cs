namespace Challenges.Function.Modules.Progress.Features.RevealHint;

// Thin handler: the ordering invariant that matters — penalty committed before the text is read
// (ADR-0045 §6) — is enforced by RevealHintAsync's conditional write, not by anything here.
public class RevealHintHandler(
    IQuestionRepository questionRepository, IPlayerProgressRepository progressRepository)
    : ICommandHandler<RevealHintCommand, RevealHintResult>
{
    public async ValueTask<RevealHintResult> Handle(RevealHintCommand command, CancellationToken cancellationToken)
    {
        var ownerId = OwnerId.Of(command.OwnerId);
        var questionId = QuestionId.Of(command.ChallengeId);

        var question = await questionRepository.GetForGradingAsync(questionId, cancellationToken)
            ?? throw new QuestionNotFoundException(command.ChallengeId);

        // Step 1 (ADR-0045 §6): the penalty is committed first. HintNotAvailableException
        // propagates as-is when the question was already answered or every hint is already spent.
        var hintsRevealed = await progressRepository.RevealHintAsync(
            ownerId, questionId, question.HintCount, cancellationToken);

        // Step 2: only reached once step 1 has committed.
        return new RevealHintResult(question.Hint(hintsRevealed), hintsRevealed, Question.HintPenalty);
    }
}
