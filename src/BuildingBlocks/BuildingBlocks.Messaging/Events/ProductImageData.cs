namespace BuildingBlocks.Messaging.Events;

// Product image metadata as it travels on integration events (ADR-0034) — the S3 key id,
// never a URL. Mirrors the Images list-of-maps attribute on the product item.
public sealed record ProductImageData(string ImageId, bool IsMain, int Order);
