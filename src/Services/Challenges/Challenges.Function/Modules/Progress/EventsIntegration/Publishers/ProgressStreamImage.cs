namespace Challenges.Function.Modules.Progress.EventsIntegration.Publishers;

// The subset of a persisted "challenge-progress" item the publisher rules reason about, projected
// from a DynamoDB Streams image. One shape covers every SK kind (PROFILE, ATTEMPT#, REDEMPTION#)
// since a single rule dispatcher runs against the whole table's stream; each rule only reads the
// fields relevant to the row kind it matches on.
//
// IsCorrect is nullable, not a plain bool: null means "not yet answered" (either no ATTEMPT row at
// all, or one that exists only as a hint-tracking placeholder — RevealHintAsync upserts
// HintsRevealed before any answer exists, ADR-0045 §6). ChallengeAnsweredRule fires on the
// transition from null to a real value, which is what lets it fire exactly once per attempt
// regardless of whether that transition arrives as an INSERT (answered with no prior hint) or a
// MODIFY (a hint was revealed first, so the row already existed).
public sealed record ProgressStreamImage(
    string OwnerId,
    string SK,
    bool? IsCorrect,
    int SelectedOption,
    int HintsRevealed,
    int PointsEarned,
    int PointsSpent)
{
    public static ProgressStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new ProgressStreamImage(
            image.TryGetValue("OwnerId", out var ownerId) ? ownerId.S : string.Empty,
            image.TryGetValue("SK", out var sk) ? sk.S : string.Empty,
            image.TryGetValue("IsCorrect", out var isCorrect) ? isCorrect.BOOL : null,
            image.TryGetValue("SelectedOption", out var selectedOption) && !string.IsNullOrEmpty(selectedOption.N)
                ? int.Parse(selectedOption.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue("HintsRevealed", out var hintsRevealed) && !string.IsNullOrEmpty(hintsRevealed.N)
                ? int.Parse(hintsRevealed.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue("PointsEarned", out var pointsEarned) && !string.IsNullOrEmpty(pointsEarned.N)
                ? int.Parse(pointsEarned.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue("PointsSpent", out var pointsSpent) && !string.IsNullOrEmpty(pointsSpent.N)
                ? int.Parse(pointsSpent.N, CultureInfo.InvariantCulture)
                : 0);
    }
}
