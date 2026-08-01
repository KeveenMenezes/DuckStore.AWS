using Challenges.Function.Modules.Progress.Features.RevealHint;

namespace Challenges.Function;

public record RevealChallengeHintRequest(string OwnerId, string ChallengeId);
public record RevealChallengeHintResponse(string Hint, int HintsRevealed, int PenaltyApplied);

// AppSync Mutation resolver (Lambda-backed per ADR-0009/ADR-0045 §6, §8 — the hint penalty is a
// conditional write that gates the response, a domain invariant that belongs in Question).
// OwnerId is injected by the resolver from ctx.identity.sub, never sent by the browser directly.
public partial class Functions
{
    [LambdaFunction]
    public async Task<RevealChallengeHintResponse> RevealChallengeHint(
        RevealChallengeHintRequest request,
        [FromServices] ISender sender)
    {
        var command = new RevealHintCommand(request.OwnerId, request.ChallengeId);
        var result = await sender.Send(command, CancellationToken.None);
        return new RevealChallengeHintResponse(result.Hint, result.HintsRevealed, result.PenaltyApplied);
    }
}
