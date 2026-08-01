using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.Events;

namespace BuildingBlocks.Messaging.Serialization;

/// <summary>
/// Compile-time JSON contracts for every integration event published to EventBridge.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on, so an event missing from this
/// list cannot be published. <see cref="EventBridge.EventBridgePublisher"/> turns that into an
/// explicit exception naming the type rather than a silent failure at invocation (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(BasketCheckoutEvent))]
[JsonSerializable(typeof(BasketCheckoutAddress))]
[JsonSerializable(typeof(BasketCheckoutItem))]
[JsonSerializable(typeof(BasketCheckoutPayment))]
[JsonSerializable(typeof(CatalogCategorySyncEvent))]
[JsonSerializable(typeof(CatalogViewProductDeletedEvent))]
[JsonSerializable(typeof(CatalogViewProductSyncedEvent))]
[JsonSerializable(typeof(ChallengeAnsweredEvent))]
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(OrderCreatedItem))]
[JsonSerializable(typeof(PaymentAuthorizedEvent))]
[JsonSerializable(typeof(PaymentDeclinedEvent))]
[JsonSerializable(typeof(PaymentRequestedEvent))]
[JsonSerializable(typeof(PointsRedeemedEvent))]
[JsonSerializable(typeof(PriceChangedEvent))]
[JsonSerializable(typeof(ProductCreatedEvent))]
[JsonSerializable(typeof(ProductDeletedEvent))]
[JsonSerializable(typeof(ProductDiscountChangedEvent))]
[JsonSerializable(typeof(ProductImageData))]
[JsonSerializable(typeof(ProductSyncedEvent))]
[JsonSerializable(typeof(ProductUpdatedEvent))]
[JsonSerializable(typeof(ReviewCreatedEvent))]
[JsonSerializable(typeof(ReviewUpdatedEvent))]
public partial class MessagingSerializerContext : JsonSerializerContext
{
}
