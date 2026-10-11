using System.Text.Json.Serialization;
using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.EventBridge;

namespace Pricing.Function.Shared.Configuration;

/// <summary>
/// Compile-time JSON contracts for every type that crosses this service's Lambda boundary.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on: DefaultLambdaJsonSerializer
/// would fail at invocation. Adding a [LambdaFunction] whose request or response type is missing
/// here is therefore a runtime error, not a compile error (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(CreateCampaignRequest))]
[JsonSerializable(typeof(CreateCampaignResponse))]
[JsonSerializable(typeof(DynamoDBEvent))]
[JsonSerializable(typeof(EndCampaignRequest))]
[JsonSerializable(typeof(EndCampaignResponse))]
[JsonSerializable(typeof(EventBridgeEvent<ProductDeletedEvent>))]
[JsonSerializable(typeof(GetBasketInstallmentPlanRequest))]
[JsonSerializable(typeof(GetBasketInstallmentPlanResponse))]
[JsonSerializable(typeof(GetInstallmentPlanRequest))]
[JsonSerializable(typeof(GetInstallmentPlanResponse))]
public partial class PricingSerializerContext : JsonSerializerContext
{
}
