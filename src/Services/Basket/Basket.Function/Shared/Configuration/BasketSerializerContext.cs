using System.Text.Json.Serialization;
using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Events;

namespace Basket.Function.Shared.Configuration;

/// <summary>
/// Compile-time JSON contracts for every type that crosses this service's Lambda boundary.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on: DefaultLambdaJsonSerializer
/// would fail at invocation. Adding a [LambdaFunction] whose request or response type is missing
/// here is therefore a runtime error, not a compile error (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(BasketCheckoutDto))]
[JsonSerializable(typeof(BasketCheckoutEvent))]
[JsonSerializable(typeof(CheckoutBasketRequest))]
[JsonSerializable(typeof(CheckoutBasketResponse))]
[JsonSerializable(typeof(DynamoDBEvent))]
[JsonSerializable(typeof(MergeBasketRequest))]
[JsonSerializable(typeof(MergeBasketResponse))]
public partial class BasketSerializerContext : JsonSerializerContext
{
}
