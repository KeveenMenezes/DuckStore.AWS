using System.ComponentModel.DataAnnotations;

namespace Managment.Web.Blazor.Services;

// Read model — mirrors the Product type in graphql/schema.graphql. Rating and price
// fields are read-only denormalizations from CatalogView/Pricing (never written here).
// Price/OriginalPrice/CashPrice/installment fields are CatalogView's calculated (post-campaign)
// values — Pricing's nominal price is fetched separately via GetNominalPriceAsync.
public sealed record Product(
    string Id,
    string Name,
    string Description,
    List<ProductImageInfo> Images,
    int Stock,
    List<string> CategoryIds,
    double AverageRating,
    int RatingCount,
    double Price,
    double OriginalPrice,
    double CashPrice,
    int MaxInstallmentsWithoutInterest,
    double MaxInstallmentValue);

// Image metadata only (ADR-0034) — display URLs are built from ImageCdn:BaseUrl + ImageId.
public sealed record ProductImageInfo(string ImageId, bool IsMain, int Order);

// One presigned POST from createProductImageUpload (ADR-0034). Fields is the AWSJSON map of
// form fields the browser must send back verbatim (policy, signature, key, Content-Type).
public sealed record PresignedImageUpload(string ImageId, string Url, string Fields);

public sealed record ProductPage(List<Product> Items, string? NextToken);

public sealed record Category(string Id, string Name, string? ParentId);

public sealed record CategoryPage(List<Category> Items, string? NextToken);

// Pricing's nominal price record (ADR-0026) — price and cost live in the Pricing
// service, managed via setNominalPrice, not on the Catalog product itself.
public sealed record PriceInfo(string ProductId, double NominalPrice, double Cost);

public sealed record CreateProductResult(string Id);

public sealed record UpdateProductResult(string Id);

public sealed record DeleteProductResult(bool IsSuccess);

// Form model — exactly the writable fields of CreateProductInput/UpdateProductInput. No stock:
// Inventory owns it, and Stock on Product is its storefront total (ADR-0052 §8).
public sealed class ProductFormModel
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    // Uploaded images (ADR-0034) — populated by the form's upload flow, not typed by the
    // admin. Invariants (≤12, exactly one main) are kept by the form and re-enforced by
    // the resolvers.
    public List<ProductImageFormModel> Images { get; set; } = [];

    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero.")]
    public double Price { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cost cannot be negative.")]
    public double Cost { get; set; }

    [MinLength(1, ErrorMessage = "Select at least one category.")]
    public List<string> CategoryIds { get; set; } = [];
}

public sealed class ProductImageFormModel
{
    public string ImageId { get; set; } = string.Empty;
    public bool IsMain { get; set; }
    public string FileName { get; set; } = string.Empty;
}

// Read model — mirrors the Campaign type in graphql/schema.graphql. StartsAt/EndsAt are ISO 8601
// strings (the wire format DynamoCampaignRepository writes with .ToString("o")).
public sealed record Campaign(
    string Id,
    string Name,
    string DiscountType,
    double Value,
    string StartsAt,
    string EndsAt,
    List<string> ProductIds,
    string Status);

public sealed record CampaignPage(List<Campaign> Items, string? NextToken);

// Form model — exactly the writable fields of createCampaign. Validation mirrors the rules
// enforced server-side by Campaign.Create/DiscountValue.Of (Pricing.Function), replicated here
// only for immediate client-side feedback.
public sealed class CampaignFormModel : IValidatableObject
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string DiscountType { get; set; } = "Fixed";

    [Range(0.01, double.MaxValue, ErrorMessage = "Value must be greater than zero.")]
    public double Value { get; set; }

    public DateTime StartsAt { get; set; } = DateTime.Now;

    public DateTime EndsAt { get; set; } = DateTime.Now.AddDays(7);

    [MinLength(1, ErrorMessage = "Select at least one product.")]
    public List<string> ProductIds { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndsAt <= StartsAt)
            yield return new ValidationResult("End date must be after the start date.", [nameof(EndsAt)]);

        if (DiscountType == "Percentage" && Value > 100)
            yield return new ValidationResult("Percentage discount cannot exceed 100.", [nameof(Value)]);
    }
}

// Read model — the admin-relevant subset of the Challenge type in graphql/schema.graphql.
// Difficulty is the upper-case enum name (EASY/MEDIUM/HARD). The answer key is never fetched.
public sealed record ChallengeSummary(
    string Id,
    string Title,
    string Language,
    string Difficulty,
    int Points);

public sealed record ChallengePage(List<ChallengeSummary> Items, string? NextToken);

// Mirrors the server rule in Mutation.updateChallengePoints (an integer from 1 to Max),
// replicated here only for immediate feedback; the resolver stays the source of truth.
public static class ChallengePointsInput
{
    public const int Max = 1000;

    public static bool TryParse(string? text, out int points, out string? error)
    {
        points = 0;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out points)
            || points <= 0 || points > Max)
        {
            error = $"Points must be a whole number from 1 to {Max}.";
            return false;
        }

        error = null;
        return true;
    }
}

// Read model — mirrors the Warehouse type in graphql/schema.graphql (ADR-0052 §2).
public sealed record Warehouse(
    string Id,
    string Code,
    string Name,
    WarehouseAddress Address,
    int Priority,
    bool Active,
    string CreatedAt,
    string UpdatedAt);

public sealed record WarehouseAddress(
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string ZipCode);

// Form model — the writable fields of CreateWarehouseInput/UpdateWarehouseInput (Code only on
// create: it is immutable). The rules mirror the resolvers (graphql/resolvers/inventory) and
// lib/warehouse-validation.ts in the SPA; the resolvers stay the authority, these only spare a
// round trip. Change all of them together.
public sealed class WarehouseFormModel
{
    public static readonly string[] States =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    ];

    [Required, RegularExpression("^ *[A-Za-z0-9][A-Za-z0-9-]{1,19} *$",
        ErrorMessage = "Code must be 2-20 characters: letters, digits or hyphens, starting with a letter or digit.")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Street { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Number { get; set; } = string.Empty;

    [StringLength(60)]
    public string? Complement { get; set; }

    [Required, StringLength(60)]
    public string District { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a state.")]
    public string State { get; set; } = string.Empty;

    [Required, RegularExpression("^ *-*([0-9]-*){8} *$", ErrorMessage = "Zip code must have 8 digits (00000-000).")]
    public string ZipCode { get; set; } = string.Empty;

    [Range(1, 999, ErrorMessage = "Priority must be between 1 and 999.")]
    public int Priority { get; set; } = 1;

    public static WarehouseFormModel From(Warehouse warehouse) => new()
    {
        Code = warehouse.Code,
        Name = warehouse.Name,
        Street = warehouse.Address.Street,
        Number = warehouse.Address.Number,
        Complement = warehouse.Address.Complement,
        District = warehouse.Address.District,
        City = warehouse.Address.City,
        State = warehouse.Address.State,
        ZipCode = warehouse.Address.ZipCode,
        Priority = warehouse.Priority,
    };
}

// Read models — mirror the stock types in graphql/schema.graphql (ADR-0052 §7). Type, Reason and
// Direction are the GraphQL enum names (Receipt, TransferOut, Count...). Quantity is signed and
// BalanceAfter is the stock on hand in that warehouse right after the movement.
public sealed record StockMovement(
    string Id,
    string ProductId,
    string? ProductName,
    string Type,
    int Quantity,
    int BalanceAfter,
    string WarehouseId,
    string WarehouseCode,
    string? InvoiceNumber,
    string? Supplier,
    string? Reason,
    string? Note,
    string? TransferId,
    string? CounterpartWarehouseId,
    string CreatedAt,
    string CreatedBy);

public sealed record StockMovementPage(List<StockMovement> Items, string? NextToken);

public sealed record StockMovementResult(List<StockMovement> Movements);

// The schema's WarehouseStock and ProductStock, renamed so they don't clash with the pages of the
// same name.
public sealed record WarehouseStockInfo(string WarehouseId, string WarehouseCode, int Available, int Reserved);

public sealed record ProductStockInfo(string ProductId, int TotalAvailable, List<WarehouseStockInfo> Warehouses);

public sealed record WarehouseProductStock(string ProductId, string? ProductName, int Available, int Reserved);

public sealed record WarehouseProductStockPage(List<WarehouseProductStock> Items, string? NextToken);

// Form models for the three stock movements. The rules mirror StockBalance in Inventory.Function,
// which stays the authority; these only spare a round trip. Change them together.
public abstract class StockMovementFormModel
{
    public const int MaxQuantity = 100_000;

    // Minted when the form opens and kept across retries, so a retry after a timeout finds the
    // movement it may already have recorded instead of recording it twice (ADR-0052 §7).
    public string IdempotencyKey { get; private set; } = Guid.NewGuid().ToString();

    [Required(ErrorMessage = "Enter a quantity.")]
    [Range(1, MaxQuantity, ErrorMessage = "Quantity must be a whole number from 1 to 100000.")]
    public int? Quantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public void RenewIdempotencyKey() => IdempotencyKey = Guid.NewGuid().ToString();
}

public sealed class StockReceiptFormModel : StockMovementFormModel
{
    [Required(ErrorMessage = "Select a warehouse.")]
    public string WarehouseId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the invoice number."), StringLength(60)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Supplier { get; set; }
}

public sealed class StockAdjustmentFormModel : StockMovementFormModel, IValidatableObject
{
    public static readonly string[] Directions = ["Increase", "Decrease"];
    public static readonly string[] Reasons = ["Count", "Loss", "Damage", "Return", "Other"];

    [Required(ErrorMessage = "Select a warehouse.")]
    public string WarehouseId { get; set; } = string.Empty;

    [Required]
    public string Direction { get; set; } = "Increase";

    [Required(ErrorMessage = "Select a reason.")]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Reason == "Other" && string.IsNullOrWhiteSpace(Note))
            yield return new ValidationResult("A note is required when the reason is Other.", [nameof(Note)]);
    }
}

public sealed class StockTransferFormModel : StockMovementFormModel, IValidatableObject
{
    [Required(ErrorMessage = "Select the origin warehouse.")]
    public string FromWarehouseId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select the destination warehouse.")]
    public string ToWarehouseId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromWarehouseId.Length > 0 && FromWarehouseId == ToWarehouseId)
            yield return new ValidationResult("The destination must differ from the origin.", [nameof(ToWarehouseId)]);
    }
}
