namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers;

// The subset of a persisted review item the publisher rules reason about, projected from a
// DynamoDB Streams image. Id is now the composite `${productId}#${userId}` key, not a Guid, so
// it's carried as a plain string. Widened beyond Id/ProductId/Rating so ReviewUpdatedRule can
// report both the old and new rating for a MODIFY. UserId (the Cognito sub, key component) and
// UserName (display-only, sourced from the Cognito name claim) are both carried here to mirror
// the full item shape, even though no rule currently reads either.
public sealed record ReviewStreamImage(
    string Id,
    Guid ProductId,
    string UserId,
    string UserName,
    string Comment,
    int Rating,
    string CreatedAt,
    string UpdatedAt)
{
    public static ReviewStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        return new ReviewStreamImage(
            image.TryGetValue("Id", out var id) ? id.S : string.Empty,
            image.TryGetValue("ProductId", out var productId) && Guid.TryParse(productId.S, out var parsed)
                ? parsed
                : Guid.Empty,
            image.TryGetValue("UserId", out var userId) ? userId.S : string.Empty,
            image.TryGetValue("UserName", out var userName) ? userName.S : string.Empty,
            image.TryGetValue("Comment", out var comment) ? comment.S : string.Empty,
            image.TryGetValue("Rating", out var rating) && !string.IsNullOrEmpty(rating.N)
                ? int.Parse(rating.N, CultureInfo.InvariantCulture)
                : 0,
            image.TryGetValue("CreatedAt", out var createdAt) ? createdAt.S : string.Empty,
            image.TryGetValue("UpdatedAt", out var updatedAt) ? updatedAt.S : string.Empty);
    }
}
