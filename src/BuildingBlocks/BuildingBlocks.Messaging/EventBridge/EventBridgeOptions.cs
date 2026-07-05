namespace BuildingBlocks.Messaging.EventBridge;

public class EventBridgeOptions
{
    public const string SectionName = "EventBridge";

    public string BusName { get; set; } = "duckstore-event-bus";
    public string Source { get; set; } = "duckstore";

    // null = auto-detect via AWS_LAMBDA_FUNCTION_NAME (see ADR-0021); set to override in either direction.
    public bool? FailFast { get; set; }
}
