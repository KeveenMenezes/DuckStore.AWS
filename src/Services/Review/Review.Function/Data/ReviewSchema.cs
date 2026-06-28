namespace Review.Function.Data;

// Single source of truth for the reviews table name and its GSI, shared by the
// development seeder (table creation) and the AppHost stream-source wiring.
public static class ReviewSchema
{
    public const string TableName = "reviews";

    // GSI1 lists a product's reviews newest-first (GSI1PK=ProductId, GSI1SK=CreatedAt),
    // backing the reviewsByProduct direct DynamoDB resolver.
    public const string Gsi1Name = "GSI1";
}
