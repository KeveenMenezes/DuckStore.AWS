namespace Challenges.Function.Modules.Progress.Features.RedeemPoints;

// Thin by design (thin-handlers-rich-domain): the debit arithmetic lives in PlayerProgress.Redeem,
// the balance invariant lives in the database's own ConditionExpression (ADR-0046 §2) — this only
// wires the two together and reports back what was persisted.
public class RedeemPointsHandler(IPlayerProgressRepository progressRepository)
    : ICommandHandler<RedeemPointsCommand, RedeemPointsResult>
{
    public async ValueTask<RedeemPointsResult> Handle(
        RedeemPointsCommand command, CancellationToken cancellationToken)
    {
        var ownerId = OwnerId.Of(command.OwnerId);

        var delta = PlayerProgress.CreateEmpty(ownerId);
        delta.Redeem(command.Points);

        var redemption = await progressRepository.RedeemPointsAsync(ownerId, delta, cancellationToken);
        var newBalance = await progressRepository.GetScoreAsync(ownerId, cancellationToken);

        return new RedeemPointsResult(redemption.Id, newBalance);
    }
}
