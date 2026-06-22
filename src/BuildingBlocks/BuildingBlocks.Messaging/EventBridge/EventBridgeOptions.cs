namespace BuildingBlocks.Messaging.EventBridge;

public class EventBridgeOptions
{
    public const string SectionName = "EventBridge";

    public string BusName { get; set; } = "duckstore-event-bus";
    public string Source { get; set; } = "duckstore";
}
