namespace Challenges.Function.Modules.Progress.Data;

// Single source of truth for the "challenge-progress" table shape (ADR-0045 §5): PK OwnerId,
// SK discriminated between PROFILE, ATTEMPT#<questionId>, and (from CH-11, not this task)
// REDEMPTION#<id>.
public static class ProgressSchema
{
    public const string TableName = "challenge-progress";
    public const string ProfileSortKey = "PROFILE";

    public const string AttemptSortKeyPrefix = "ATTEMPT#";

    // CH-11 (redemption, not this task) writes REDEMPTION#<id> rows; the stream-publisher rule
    // that reacts to them is built here, alongside ChallengeAnsweredRule (ADR-0046 §3).
    public const string RedemptionSortKeyPrefix = "REDEMPTION#";

    // Per-language correct-answer counters live as sparse top-level attributes on PROFILE
    // (Lang#python, Lang#javascript, ...) rather than as a nested ByLanguage map. A nested-map ADD
    // requires the parent map to already exist, which CatalogView's RatingDistribution can assume
    // (a product is always created before it can be reviewed) but PlayerProgress cannot: a
    // player's very first submitted answer must both create PROFILE and bump its language counter
    // in the same write. Flat attributes need no parent to pre-exist, so the delta-only write
    // (ADR-0045 §5) stays a single ADD either way. The GraphQL/AppSync layer reassembles these
    // into the ByLanguage AWSJSON map clients see.
    private const string LanguageAttributePrefix = "Lang#";

    public static string AttemptSortKey(string questionId) => $"{AttemptSortKeyPrefix}{questionId}";

    public static string ParseAttemptQuestionId(string attemptSortKey) =>
        attemptSortKey[AttemptSortKeyPrefix.Length..];

    public static string RedemptionSortKey(string redemptionId) => $"{RedemptionSortKeyPrefix}{redemptionId}";

    public static string ParseRedemptionId(string redemptionSortKey) =>
        redemptionSortKey[RedemptionSortKeyPrefix.Length..];

    public static string LanguageAttribute(string language) => $"{LanguageAttributePrefix}{language}";
}
