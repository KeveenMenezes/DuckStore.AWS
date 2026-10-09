using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.Function.Modules.Reviews.Data;

public sealed class DynamoReviewEligibilityRepository(IAmazonDynamoDB dynamoDb) : IReviewEligibilityRepository
{
    public async Task<bool> TryCreateEligibleAsync(
        Guid productId, Guid customerId, DateTime createdAtUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            await dynamoDb.PutItemAsync(ToEligiblePutItemRequest(productId, customerId, createdAtUtc), cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            // The customer already has a row for this product: a repeat purchase never resets it.
            return false;
        }
    }

    // One individual conditional PutItem per product, never a TransactWriteItems: in a transaction
    // a single already-reviewed product would cancel the eligibility of every other product in the
    // order (ADR-0049 §3). The condition alone makes redelivery idempotent, so no inbox is needed.
    // No Rating/Comment, and no GSI1PK/GSI1SK — keeping an Eligible row out of the sparse GSI1 is
    // what hides it from reviewsByProduct (ADR-0049 §2). Attribute types match
    // Mutation.createReview.upsert.js: ProductId/UserId/CreatedAt are all strings.
    internal static PutItemRequest ToEligiblePutItemRequest(Guid productId, Guid customerId, DateTime createdAtUtc)
    {
        var product = productId.ToString();
        var user = customerId.ToString();

        return new PutItemRequest
        {
            TableName = ReviewSchema.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new() { S = ReviewSchema.ComposeId(product, user) },
                ["ProductId"] = new() { S = product },
                ["UserId"] = new() { S = user },
                [ReviewSchema.StatusAttribute] = new() { S = ReviewStatus.Eligible.ToString() },
                // Millisecond ISO-8601, the shape of AppSync's util.time.nowISO8601(): createReview
                // copies CreatedAt into GSI1SK, so both writers must sort the same way.
                ["CreatedAt"] = new()
                {
                    S = createdAtUtc.ToUniversalTime()
                        .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                }
            },
            ConditionExpression = "attribute_not_exists(Id)"
        };
    }
}
