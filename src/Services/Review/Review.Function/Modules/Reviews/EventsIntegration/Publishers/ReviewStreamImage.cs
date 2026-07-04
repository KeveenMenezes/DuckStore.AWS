namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers;

public sealed record ReviewStreamImage(Guid Id, Guid ProductId, int Rating)
{
    public static ReviewStreamImage From(Dictionary<string, DynamoDBEvent.AttributeValue> image) => new(
        Guid.Parse(image["Id"].S),
        Guid.Parse(image["ProductId"].S),
        int.Parse(image["Rating"].N));
}
