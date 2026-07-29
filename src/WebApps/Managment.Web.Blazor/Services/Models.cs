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

// Form model — exactly the writable fields of CreateProductInput/UpdateProductInput.
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

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

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
