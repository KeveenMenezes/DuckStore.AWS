namespace Ordering.Function.Modules.Orders.EventsIntegration.Publishers;

// The subset of a persisted order item the publisher rules reason about, projected from a
// DynamoDB Streams image. Carrying Status (not just Id/Type) is deliberate: it lets forward
// -looking rules react to fulfillment transitions (e.g. Pending -> Approved) by comparing the
// old and new images, without the rules ever touching raw AttributeValue maps.
public sealed record OrderStreamImage(Guid Id, string Type, string Status)
{
    public static OrderStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        var id = image.TryGetValue("Id", out var idValue) && Guid.TryParse(idValue.S, out var guid)
            ? guid
            : Guid.Empty;

        return new OrderStreamImage(
            id,
            image.TryGetValue("Type", out var type) ? type.S : string.Empty,
            image.TryGetValue("Status", out var status) ? status.S : string.Empty);
    }
}
