namespace Payment.Function.Modules.Payments.EventsIntegration.Publishers;

// The subset of a persisted payment the publisher rules reason about, projected from a
// DynamoDB Streams image — rules never touch raw AttributeValue maps.
public sealed record PaymentStreamImage(Guid Id, string Type, string Status)
{
    public static PaymentStreamImage? From(Dictionary<string, DynamoDBEvent.AttributeValue>? image)
    {
        if (image is null || image.Count == 0)
            return null;

        var id = image.TryGetValue("Id", out var idValue) && Guid.TryParse(idValue.S, out var guid)
            ? guid
            : Guid.Empty;

        return new PaymentStreamImage(
            id,
            image.TryGetValue("Type", out var type) ? type.S : string.Empty,
            image.TryGetValue("Status", out var status) ? status.S : string.Empty);
    }
}
