using Challenges.Function.Modules.Progress.Features.SubmitAnswer;

namespace Challenges.Function;

public record SubmitChallengeAnswerRequest(string OwnerId, string ChallengeId, int SelectedOption);
public record SubmitChallengeAnswerResponse(
    bool IsCorrect, int PointsEarned, int NewScore, string Explanation, int SelectedOption);

// AppSync Mutation resolver (Lambda-backed per ADR-0009/ADR-0045 §8 — grading needs the stored
// answer key and a multi-item transaction, both beyond a direct resolver). OwnerId is injected by
// the resolver from ctx.identity.sub, never sent by the browser as a free-form argument.
public partial class Functions
{
    [LambdaFunction]
    public async Task<SubmitChallengeAnswerResponse> SubmitChallengeAnswer(
        SubmitChallengeAnswerRequest request,
        [FromServices] ISender sender)
    {
        var command = new SubmitAnswerCommand(request.OwnerId, request.ChallengeId, request.SelectedOption);
        var result = await sender.Send(command, CancellationToken.None);
        return new SubmitChallengeAnswerResponse(
            result.IsCorrect, result.PointsEarned, result.NewScore, result.Explanation, result.SelectedOption);
    }
}
