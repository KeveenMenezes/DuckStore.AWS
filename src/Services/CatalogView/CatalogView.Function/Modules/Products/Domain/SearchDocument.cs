using System.Text.Json.Serialization;

namespace CatalogView.Function.Modules.Products.Domain;

// One item per product in DynamoDB's "catalogview-products" table (ADR-0030). RatingSum and
// LastRatingEventId are internal bookkeeping — never surfaced to GraphQL/AppSync callers.
public sealed class SearchDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    // Image metadata only (ADR-0034) — keys, never URLs; clients build display URLs from
    // configuration + ImageId. Empty for products created before the image pipeline.
    [JsonPropertyName("images")]
    public List<ImageRef> Images { get; set; } = [];

    // The sticker/"De" price (was "price") — never discounted, also the ceiling the store
    // subsidizes card-installment fees up to (ADR-0028 supersession).
    [JsonPropertyName("originalPrice")]
    public decimal OriginalPrice { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }

    [JsonPropertyName("categoryIds")]
    public List<string> CategoryIds { get; set; } = [];

    // Denormalized display names, index-aligned with CategoryIds — kept in sync with Catalog's
    // categories table by CategorySyncHandler whenever a category is renamed (ADR-0027 extension).
    [JsonPropertyName("categories")]
    public List<CategoryRef> Categories { get; set; } = [];

    [JsonPropertyName("averageRating")]
    public double AverageRating { get; set; }

    [JsonPropertyName("ratingCount")]
    public int RatingCount { get; set; }

    // Payment highlights — computed by Pricing from cost + the active GatewayCost provider (and
    // any active campaign discount), carried on PriceChangedEvent and merged whenever the price
    // changes.
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("cashPrice")]
    public decimal CashPrice { get; set; }

    [JsonPropertyName("maxInstallmentsWithoutInterest")]
    public int MaxInstallmentsWithoutInterest { get; set; }

    // The per-installment $ value at MaxInstallmentsWithoutInterest — carried as a lone scalar
    // (not the full plan array) so the catalog card can show an exact "up to Nx of $Y" line
    // without pulling the entire per-installment breakdown into the search index.
    [JsonPropertyName("maxInstallmentValue")]
    public decimal MaxInstallmentValue { get; set; }

    // Internal — the running sum backing AverageRating. DynamoDB can't divide two attributes in
    // one UpdateExpression, so RatingSum/RatingCount are accumulated atomically and the average is
    // recomputed in a second step (ADR-0030 — see DynamoProductIndex).
    [JsonPropertyName("ratingSum")]
    public int RatingSum { get; set; }

    // Internal — idempotency marker for the rating-aggregation update (see
    // DynamoProductIndex.ApplyRatingAsync). A redelivered ReviewCreated event with the same
    // eventId is a no-op instead of double-counting.
    [JsonPropertyName("lastRatingEventId")]
    public string? LastRatingEventId { get; set; }
}

// One entry per category a product directly belongs to — id for filtering, name for display
// (breadcrumb-ready once the category hierarchy is surfaced through GraphQL).
public sealed record CategoryRef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

// One entry per product image, mirrored from Catalog's Images attribute via
// ProductSyncedEvent (ADR-0034).
public sealed record ImageRef(
    [property: JsonPropertyName("imageId")] string ImageId,
    [property: JsonPropertyName("isMain")] bool IsMain,
    [property: JsonPropertyName("order")] int Order);
