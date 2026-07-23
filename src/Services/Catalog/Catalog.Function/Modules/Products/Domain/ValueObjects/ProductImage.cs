namespace Catalog.Function.Modules.Products.Domain.ValueObjects;

// Image metadata only (ADR-0034) — the key of an already-uploaded S3 original, never a URL.
// Clients build display URLs from configuration + ImageId.
public sealed record ProductImage(string ImageId, bool IsMain, int Order);
