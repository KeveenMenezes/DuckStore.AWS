using System.Text.Json.Serialization;
using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;

namespace Payment.Function.Shared.Configuration;

/// <summary>
/// Compile-time JSON contracts for every type that crosses this service's Lambda boundary.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on: DefaultLambdaJsonSerializer
/// would fail at invocation. Adding a [LambdaFunction] whose request or response type is missing
/// here is therefore a runtime error, not a compile error (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(DynamoDBEvent))]
[JsonSerializable(typeof(RegisterPaymentRequest))]
[JsonSerializable(typeof(RegisterPaymentResponse))]
[JsonSerializable(typeof(VoidPaymentRequest))]
[JsonSerializable(typeof(VoidPaymentResponse))]
[JsonSerializable(typeof(EventBridgeEvent<BasketCheckoutEvent>))]
[JsonSerializable(typeof(EventBridgeEvent<PaymentAuthorizedEvent>))]
[JsonSerializable(typeof(EventBridgeEvent<PaymentDeclinedEvent>))]
[JsonSerializable(typeof(EventBridgeEvent<PointsReservedEvent>))]
[JsonSerializable(typeof(EventBridgeEvent<PointsReservationFailedEvent>))]
public partial class PaymentSerializerContext : JsonSerializerContext
{
}
