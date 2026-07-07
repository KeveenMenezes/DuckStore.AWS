using System.Text;

namespace Review.Function.Modules.Reviews.Data;

// Single source of truth for the reviews table name and its GSI, shared by the
// development seeder (table creation) and the AppHost stream-source wiring.
public static class ReviewSchema
{
    public const string TableName = "reviews";

    // GSI1 lists a product's reviews newest-first (GSI1PK=ProductId, GSI1SK=CreatedAt),
    // backing the reviewsByProduct direct DynamoDB resolver.
    public const string Gsi1Name = "GSI1";

    // The canonical composite Id: one row per (ProductId, UserName), so a PutItem is naturally
    // an upsert (ADR-0029) — enforces "one active review per customer per product" structurally,
    // with no extra GSI or conditional write needed. UserName is standard base64-encoded since
    // it's an arbitrary user-supplied string that could otherwise collide with the '#' separator;
    // ProductId is always a GUID and never needs encoding. Plain (not url-safe) base64 is fine —
    // this is a DynamoDB key, never a URL segment — and matches AppSync JS's util.base64Encode and
    // Node's Buffer('base64') exactly, which the production write path (the AppSync JS pipeline
    // resolver) and the local dev GraphQL backend (app/api/graphql/local.ts) both use directly.
    public static string ComposeId(string productId, string userName) =>
        $"{productId}#{Convert.ToBase64String(Encoding.UTF8.GetBytes(userName))}";
}
