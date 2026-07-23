namespace BuildingBlocks.Messaging.EventBridge;

// Thrown by EventBridgePublisher when running on AWS (see ADR-0021) so a partial-failure
// PutEvents response is not indistinguishable from success to the caller.
public sealed class EventPublishException(string detailType, string? errorCode, string? errorMessage)
    : Exception($"Failed to publish {detailType} to EventBridge: {errorCode} - {errorMessage}");
