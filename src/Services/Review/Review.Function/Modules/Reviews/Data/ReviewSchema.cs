using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.Function.Modules.Reviews.Data;

// Single source of truth for the reviews table name and its GSI, shared by the
// development seeder (table creation) and the AppHost stream-source wiring.
public static class ReviewSchema
{
    public const string TableName = "reviews";

    // GSI1 lists a product's reviews newest-first (GSI1PK=ProductId, GSI1SK=CreatedAt),
    // backing the reviewsByProduct direct DynamoDB resolver.
    public const string Gsi1Name = "GSI1";

    // GSI1 is sparse (ADR-0049 §2): only Published rows carry these two attributes.
    public const string Gsi1PkAttribute = "GSI1PK";
    public const string Gsi1SkAttribute = "GSI1SK";

    // ReviewStatus as a string. Absent on rows written before ADR-0049 (see EffectiveStatus).
    public const string StatusAttribute = "Status";

    // TTL attribute (epoch seconds), set only on Deleted rows so they expire 5 days later.
    public const string ExpiresAtAttribute = "ExpiresAt";

    // The canonical composite Id: one row per (ProductId, UserId), so a PutItem is naturally
    // an upsert — enforces "one active review per customer per product" structurally, with no
    // extra GSI or conditional write needed. UserId is the Cognito sub, derived server-side from
    // ctx.identity.sub (never client-supplied) — this is what closes the spoofing bug the prior
    // base64(userName) scheme had, where a client could pass any display-name string and overwrite
    // another user's review by guessing it. No encoding is needed: a Cognito sub is always a UUID
    // and never collides with the '#' separator. This matches the AppSync JS pipeline resolver
    // (Mutation.createReview.checkExisting.js/upsert.js) and the local dev GraphQL backend
    // (app/api/graphql/local.ts), which both compose the same id directly.
    public static string ComposeId(string productId, string userId) => $"{productId}#{userId}";

    // Rows written before ADR-0049 have no Status and are already in GSI1, so they are treated as
    // Published — this is what keeps legacy reviews working with no migration.
    public static ReviewStatus EffectiveStatus(string? status) =>
        string.IsNullOrEmpty(status) ? ReviewStatus.Published : Enum.Parse<ReviewStatus>(status);
}
