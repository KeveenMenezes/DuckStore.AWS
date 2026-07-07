using System.Text.Json;
using OpenSearch.Net;

namespace CatalogView.Function.Modules.Products.Data;

// OpenSearch-backed implementation of the products search index (ADR-0027). CatalogView owns no
// DynamoDB table — this is its only datastore. Uses the low-level OpenSearch.Net client with hand
// -built request bodies: no strongly-typed OpenSearch.Client dependency needed for the handful of
// operations this service performs (index/update/delete/search).
public sealed class OpenSearchProductIndex(IOpenSearchLowLevelClient client) : IProductSearchIndex
{
    public const string IndexName = "products";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        // Partial merge (OpenSearch _update with doc_as_upsert), not a full Index/PUT — a product
        // edit must never clobber the rating fields (averageRating/ratingCount/ratingSum/
        // lastRatingEventId) maintained by ApplyRatingAsync, nor the price maintained by
        // ApplyPriceAsync (Pricing owns it — ADR-0026). This mirrors the same invariant
        // Catalog's own DynamoProductRepository.ToItem used to preserve before ADR-0027.
        var body = PostData.Serializable(new
        {
            doc = new
            {
                id = document.Id,
                name = document.Name,
                description = document.Description,
                imageUrl = document.ImageUrl,
                stock = document.Stock,
                categoryIds = document.CategoryIds
            },
            doc_as_upsert = true
        });

        await client.UpdateAsync<StringResponse>(IndexName, document.Id, body, ctx: cancellationToken);
    }

    public async Task ApplyPricingAsync(
        string productId,
        decimal originalPrice,
        decimal price,
        decimal cashPrice,
        int maxInstallmentsWithoutInterest,
        decimal maxInstallmentValue,
        CancellationToken cancellationToken = default)
    {
        var body = PostData.Serializable(new
        {
            doc = new { originalPrice, price, cashPrice, maxInstallmentsWithoutInterest, maxInstallmentValue },
            doc_as_upsert = true
        });

        await client.UpdateAsync<StringResponse>(IndexName, productId, body, ctx: cancellationToken);
    }

    public async Task DeleteAsync(string productId, CancellationToken cancellationToken = default) =>
        await client.DeleteAsync<StringResponse>(IndexName, productId, ctx: cancellationToken);

    // A single Painless scripted update does the idempotency check, the sum/count accumulation and
    // the average recomputation atomically — replacing Catalog's old two-step GetItem+UpdateItem
    // approach (ADR-0011 §4, now superseded by ADR-0027).
    public async Task ApplyRatingAsync(
        string productId, string eventId, int rating, CancellationToken cancellationToken = default)
    {
        const string script = """
            if (ctx._source.lastRatingEventId == params.eventId) {
                ctx.op = 'none';
            } else {
                ctx._source.ratingSum = (ctx._source.ratingSum ?: 0) + params.rating;
                ctx._source.ratingCount = (ctx._source.ratingCount ?: 0) + 1;
                ctx._source.averageRating = (double) ctx._source.ratingSum / ctx._source.ratingCount;
                ctx._source.lastRatingEventId = params.eventId;
            }
            """;

        var body = PostData.Serializable(new
        {
            script = new
            {
                source = script,
                lang = "painless",
                @params = new { eventId, rating }
            }
        });

        await client.UpdateAsync<StringResponse>(IndexName, productId, body, ctx: cancellationToken);
    }

    // Edit path (ADR-0029): ratingCount is untouched, ratingSum moves by (newRating - oldRating).
    // The ratingCount==0 guard is defensive insurance against a MODIFY being processed before its
    // corresponding INSERT under out-of-order CDC delivery — shouldn't be reachable given the
    // reviews table's composite-key uniqueness invariant (an edit implies a prior insert), but two
    // independent consumer Lambdas give no ordering guarantee.
    public async Task ApplyRatingUpdateAsync(
        string productId, string eventId, int oldRating, int newRating,
        CancellationToken cancellationToken = default)
    {
        const string script = """
            if (ctx._source.lastRatingEventId == params.eventId) {
                ctx.op = 'none';
            } else {
                ctx._source.ratingSum = (ctx._source.ratingSum ?: 0) + (params.newRating - params.oldRating);
                ctx._source.averageRating = (ctx._source.ratingCount ?: 0) == 0
                    ? 0
                    : (double) ctx._source.ratingSum / ctx._source.ratingCount;
                ctx._source.lastRatingEventId = params.eventId;
            }
            """;

        var body = PostData.Serializable(new
        {
            script = new
            {
                source = script,
                lang = "painless",
                @params = new { eventId, oldRating, newRating }
            }
        });

        await client.UpdateAsync<StringResponse>(IndexName, productId, body, ctx: cancellationToken);
    }

    public async Task<SearchDocument?> GetAsync(string productId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync<StringResponse>(IndexName, productId, ctx: cancellationToken);

        if (!response.Success || response.Body is null)
            return null;

        using var doc = JsonDocument.Parse(response.Body);

        if (!doc.RootElement.TryGetProperty("found", out var found) || !found.GetBoolean())
            return null;

        return doc.RootElement.GetProperty("_source").Deserialize<SearchDocument>(s_json);
    }

    public async Task<ProductSearchResult> SearchAsync(
        ProductSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var from = DecodeFrom(criteria.NextToken);

        var must = new List<object>();

        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            must.Add(new
            {
                multi_match = new { query = criteria.Query, fields = new[] { "name^2", "description" } }
            });
        }

        if (criteria.MinRating is not null || criteria.MaxRating is not null)
        {
            must.Add(new
            {
                range = new
                {
                    averageRating = new
                    {
                        gte = criteria.MinRating,
                        lte = criteria.MaxRating
                    }
                }
            });
        }

        object queryClause = must.Count == 0
            ? new { match_all = new { } }
            : new { @bool = new { must } };

        var sort = criteria.SortBy switch
        {
            ProductSortField.Price => new object[] { new { price = new { order = criteria.Descending ? "desc" : "asc" } } },
            ProductSortField.AverageRating => new object[]
            {
                new { averageRating = new { order = criteria.Descending ? "desc" : "asc" } }
            },
            _ => []
        };

        var body = PostData.Serializable(new
        {
            from,
            size = criteria.PageSize,
            query = queryClause,
            sort
        });

        var response = await client.SearchAsync<StringResponse>(IndexName, body, ctx: cancellationToken);

        if (!response.Success || response.Body is null)
            return new ProductSearchResult([], null);

        using var doc = JsonDocument.Parse(response.Body);
        var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");

        var items = new List<SearchDocument>();
        foreach (var hit in hits.EnumerateArray())
        {
            var source = hit.GetProperty("_source").Deserialize<SearchDocument>(s_json);
            if (source is not null)
                items.Add(source);
        }

        var nextToken = items.Count == criteria.PageSize
            ? EncodeFrom(from + items.Count)
            : null;

        return new ProductSearchResult(items, nextToken);
    }

    private static int DecodeFrom(string? nextToken)
    {
        if (string.IsNullOrEmpty(nextToken))
            return 0;

        return int.TryParse(nextToken, out var from) ? from : 0;
    }

    private static string EncodeFrom(int from) => from.ToString();

    // Free-tier mapping (ADR-0027 cost checklist): only the aggregated rating fields, never full
    // review text — keeps storage well under the 10GB single-node free tier limit.
    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var exists = await client.Indices.ExistsAsync<StringResponse>(IndexName, ctx: cancellationToken);
        if (exists.Success)
            return;

        var body = PostData.Serializable(new
        {
            mappings = new
            {
                properties = new
                {
                    id = new { type = "keyword" },
                    name = new { type = "text" },
                    description = new { type = "text" },
                    imageUrl = new { type = "keyword", index = false },
                    originalPrice = new { type = "double" },
                    price = new { type = "double" },
                    stock = new { type = "integer" },
                    categoryIds = new { type = "keyword" },
                    averageRating = new { type = "double" },
                    ratingCount = new { type = "integer" },
                    cashPrice = new { type = "double" },
                    maxInstallmentsWithoutInterest = new { type = "integer" },
                    maxInstallmentValue = new { type = "double" },
                    ratingSum = new { type = "integer" },
                    lastRatingEventId = new { type = "keyword" }
                }
            }
        });

        await client.Indices.CreateAsync<StringResponse>(IndexName, body, ctx: cancellationToken);
    }

    public async Task BulkIndexAsync(
        IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();

        foreach (var document in documents)
        {
            lines.Add(JsonSerializer.Serialize(new { index = new { _index = IndexName, _id = document.Id } }));
            lines.Add(JsonSerializer.Serialize(document, s_json));
        }

        if (lines.Count == 0)
            return;

        var ndjson = string.Join('\n', lines) + '\n';
        await client.BulkAsync<StringResponse>(PostData.String(ndjson), ctx: cancellationToken);
    }
}
