using System.Text.Json.Serialization;

namespace Ordering.BasketCheckoutConsumer.Lambda;

public class EventBridgeEvent<TDetail>
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("source")]
    public string Source { get; set; } = default!;

    [JsonPropertyName("detail-type")]
    public string DetailType { get; set; } = default!;

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("detail")]
    public TDetail Detail { get; set; } = default!;
}
