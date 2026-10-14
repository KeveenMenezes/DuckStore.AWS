using System.Text.Json.Serialization;
using Amazon.Lambda.DynamoDBEvents;
using Inventory.Function.Modules.Stock.Features;

namespace Inventory.Function.Shared.Configuration;

/// <summary>
/// Compile-time JSON contracts for every type that crosses this service's Lambda boundary.
/// </summary>
/// <remarks>
/// Native AOT has no reflection-based serializer to fall back on, so a [LambdaFunction] whose request
/// or response type is missing here fails at invocation, not at compile time (ADR-0042 §7).
/// </remarks>
[JsonSerializable(typeof(DynamoDBEvent))]
[JsonSerializable(typeof(RecordStockReceiptRequest))]
[JsonSerializable(typeof(RecordStockAdjustmentRequest))]
[JsonSerializable(typeof(TransferStockRequest))]
[JsonSerializable(typeof(StockMovementResponse))]
public partial class InventorySerializerContext : JsonSerializerContext
{
}
