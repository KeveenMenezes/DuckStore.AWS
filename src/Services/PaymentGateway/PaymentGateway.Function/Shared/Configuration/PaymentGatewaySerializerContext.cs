using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.EventBridge;

namespace PaymentGateway.Function.Shared.Configuration;

/// <summary>
/// Compile-time JSON contracts for every type that crosses this service's Lambda boundary.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on: DefaultLambdaJsonSerializer
/// would fail at invocation. Adding a [LambdaFunction] whose request or response type is missing
/// here is therefore a runtime error, not a compile error (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(EventBridgeEvent<PaymentRequestedEvent>))]
public partial class PaymentGatewaySerializerContext : JsonSerializerContext
{
}
