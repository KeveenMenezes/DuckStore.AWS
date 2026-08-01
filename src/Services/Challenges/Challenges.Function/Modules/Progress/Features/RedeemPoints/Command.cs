namespace Challenges.Function.Modules.Progress.Features.RedeemPoints;

// Contract is deliberately narrow (ADR-0046 §1): a point quantity only. No currency amount is
// ever accepted, computed or returned here — what a point is worth is Pricing's decision, made
// later and asynchronously, off the PointsRedeemedEvent this redemption's write triggers.
public record RedeemPointsCommand(string OwnerId, int Points) : ICommand<RedeemPointsResult>;

public record RedeemPointsResult(string RedemptionId, int NewBalance);

public class RedeemPointsCommandValidator : IValidator<RedeemPointsCommand>
{
    public IEnumerable<ValidationFailure> Validate(RedeemPointsCommand instance)
    {
        if (string.IsNullOrWhiteSpace(instance.OwnerId))
        {
            yield return new(nameof(instance.OwnerId), "OwnerId is required");
        }

        if (instance.Points < PlayerProgress.MinimumRedeemablePoints)
        {
            yield return new(
                nameof(instance.Points),
                $"Points must be at least {PlayerProgress.MinimumRedeemablePoints}");
        }
    }
}
