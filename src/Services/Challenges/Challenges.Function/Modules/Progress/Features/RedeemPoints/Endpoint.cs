using Challenges.Function.Modules.Progress.Features.RedeemPoints;

namespace Challenges.Function;

public record RedeemChallengePointsRequest(string OwnerId, int Points);
public record RedeemChallengePointsResponse(string RedemptionId, int NewBalance);

// AppSync Mutation resolver (Lambda-backed per ADR-0009/ADR-0046 §2 — the balance debit is a
// conditional TransactWriteItems, an invariant that belongs in the domain, not a resolver script).
// OwnerId is injected by the resolver from ctx.identity.sub, never sent by the browser directly.
public partial class Functions
{
    [LambdaFunction]
    public async Task<RedeemChallengePointsResponse> RedeemChallengePoints(
        RedeemChallengePointsRequest request,
        [FromServices] ISender sender)
    {
        var command = new RedeemPointsCommand(request.OwnerId, request.Points);
        var result = await sender.Send(command, CancellationToken.None);
        return new RedeemChallengePointsResponse(result.RedemptionId, result.NewBalance);
    }
}
