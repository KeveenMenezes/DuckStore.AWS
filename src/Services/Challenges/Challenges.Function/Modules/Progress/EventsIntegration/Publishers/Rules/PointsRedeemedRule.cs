using Challenges.Function.Modules.Progress.Data;

namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers.Rules;

// Fires on a new REDEMPTION# row (CH-11, not this task — the rule is born here alongside the
// publisher per ADR-0046 §3, with no consumer until CH-12). Unlike ATTEMPT#, a redemption row is
// written once with attribute_not_exists(SK) and never updated again, so a plain INSERT match is
// enough — no transition to detect. No currency field ever appears: Challenges publishes what was
// spent, Pricing decides what it's worth (ADR-0046 §1) — adding an amount here is NOT ALLOWED.
public sealed class PointsRedeemedRule : IStreamRule<ProgressStreamImage>
{
    public bool Match(StreamContext<ProgressStreamImage> context) =>
        context.EventName == "INSERT"
        && (context.New?.SK.StartsWith(ProgressSchema.RedemptionSortKeyPrefix, StringComparison.Ordinal) ?? false);

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ProgressStreamImage> context, CancellationToken cancellationToken = default)
    {
        var redemption = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(PointsRedeemedEvent),
            new PointsRedeemedEvent
            {
                OwnerId = redemption.OwnerId,
                RedemptionId = ProgressSchema.ParseRedemptionId(redemption.SK),
                Points = redemption.PointsSpent
            }));
    }
}
