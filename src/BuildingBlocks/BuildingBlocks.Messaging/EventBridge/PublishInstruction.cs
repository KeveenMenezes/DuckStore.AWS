namespace BuildingBlocks.Messaging.EventBridge;

// A transport-agnostic "publish this" instruction produced by an IStreamRule: it names the
// EventBridge detail-type and carries the payload, but knows nothing about EventBridge itself.
// Converting it into a PutEventsRequest is the sole responsibility of IEventPublisher, so rules
// never take a dependency on AWS infrastructure.
public sealed record PublishInstruction(string DetailType, object Payload);
