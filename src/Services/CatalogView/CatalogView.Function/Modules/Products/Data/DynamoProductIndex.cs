﻿using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CatalogView.Function.Modules.Products.Domain;

namespace CatalogView.Function.Modules.Products.Data;

// DynamoDB-backed implementation of the products search index (ADR-0030). CatalogView owns its
// own table, "catalogview-products" — this is its only datastore. Uses the same low-level
// IAmazonDynamoDB + raw AttributeValue style as
// Catalog.Function's DynamoProductRepository/Pricing.Function's DynamoCampaignRepository.
public sealed class DynamoProductIndex(IAmazonDynamoDB dynamoDb) : IProductSearchIndex
{
    public const string TableName = "catalogview-products";

    // GSI1PK is a constant ("PRODUCT"), GSI1SK = AverageRating — lets the unfiltered/rating-only
    // browse path Query instead of Scan (see infra/constructs/catalogview-dynamodb.ts).
    public const string Gsi1Name = "GSI1";
    private const string Gsi1PartitionValue = "PRODUCT";

    // "Name" is a DynamoDB reserved word; "Description" is not, but both are aliased for
    // consistency/readability in the expressions below.
    private const string NameAlias = "#Name";
    private const string DescriptionAlias = "#Description";

    public Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        // Partial merge (UpdateItem, not a full PutItem) — a product edit must never clobber the
        // rating fields (AverageRating/RatingCount/RatingSum/LastRatingEventId) maintained by
        // ApplyRatingAsync, nor the price fields maintained by ApplyPricingAsync (Pricing owns
        // them — ADR-0026). This mirrors the same invariant Catalog's own
        // DynamoProductRepository.ToItem preserves.
        var request = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(document.Id) },
            UpdateExpression =
                $"SET {NameAlias} = :name, {DescriptionAlias} = :description, Images = :images, " +
                "Stock = :stock, CategoryIds = :categoryIds, Categories = :categories, " +
                "GSI1PK = :gsi1pk, GSI1SK = if_not_exists(GSI1SK, :zero)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                [NameAlias] = "Name",
                [DescriptionAlias] = "Description"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":name"] = new(document.Name),
                [":description"] = new(document.Description),
                [":images"] = ToImageList(document.Images),
                [":stock"] = new AttributeValue { N = document.Stock.ToString(CultureInfo.InvariantCulture) },
                [":categoryIds"] = ToStringList(document.CategoryIds),
                [":categories"] = ToCategoryList(document.Categories),
                [":gsi1pk"] = new(Gsi1PartitionValue),
                // if_not_exists keeps this an insert-only default — must never clobber the
                // AverageRating-derived GSI1SK that RecomputeAverageAsync maintains afterwards.
                [":zero"] = new AttributeValue { N = "0" }
            }
        };

        return dynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public Task DeleteAsync(string productId, CancellationToken cancellationToken = default) =>
        dynamoDb.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) }
            },
            cancellationToken);

    // Scan + per-matching-item UpdateItem (ADR-0030 — DynamoDB has no _update_by_query
    // equivalent). Acceptable at this catalog's scale; see ADR-0030 "Negative/Costs".
    public async Task RenameCategoryAsync(
        string categoryId, string name, CancellationToken cancellationToken = default)
    {
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var scanResponse = await dynamoDb.ScanAsync(
                new ScanRequest
                {
                    TableName = TableName,
                    ExclusiveStartKey = lastKey,
                    FilterExpression = "contains(CategoryIds, :categoryId)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":categoryId"] = new(categoryId)
                    }
                },
                cancellationToken);

            foreach (var item in scanResponse.Items ?? [])
                await RenameCategoryOnItemAsync(item, categoryId, name, cancellationToken);

            lastKey = scanResponse.LastEvaluatedKey is { Count: > 0 } ? scanResponse.LastEvaluatedKey : null;
        } while (lastKey is not null);
    }

    private async Task RenameCategoryOnItemAsync(
        Dictionary<string, AttributeValue> item,
        string categoryId,
        string name,
        CancellationToken cancellationToken)
    {
        if (!item.TryGetValue("Categories", out var categoriesAttr) || categoriesAttr.L is null)
            return;

        var index = categoriesAttr.L.FindIndex(
            category => category.M is not null &&
                        category.M.TryGetValue("Id", out var id) &&
                        id.S == categoryId);

        if (index < 0)
            return;

        await dynamoDb.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = item["Id"] },
                UpdateExpression = $"SET Categories[{index}].{NameAlias} = :name",
                ExpressionAttributeNames = new Dictionary<string, string> { [NameAlias] = "Name" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":name"] = new(name) }
            },
            cancellationToken);
    }

    // Idempotent two-step update: a conditional ADD accumulates RatingSum/RatingCount only if
    // LastRatingEventId doesn't already match eventId (idempotency guard); a second read+recompute
    // sets AverageRating, since DynamoDB update expressions can't divide two attributes directly
    // (ADR-0030 accepted trade-off — mirrors ADR-0011 §4's original two-step approach).
    public async Task ApplyRatingAsync(
        string productId, string eventId, int rating, CancellationToken cancellationToken = default)
    {
        var applied = await TryApplyRatingDeltaAsync(
            productId, eventId, ratingSumDelta: rating, ratingCountDelta: 1, cancellationToken);

        if (applied)
            await RecomputeAverageAsync(productId, cancellationToken);
    }

    // Sibling to ApplyRatingAsync for the edit path (ADR-0029): RatingCount stays unchanged,
    // RatingSum moves by (newRating - oldRating).
    public async Task ApplyRatingUpdateAsync(
        string productId, string eventId, int oldRating, int newRating,
        CancellationToken cancellationToken = default)
    {
        var applied = await TryApplyRatingDeltaAsync(
            productId, eventId, ratingSumDelta: newRating - oldRating, ratingCountDelta: 0, cancellationToken);

        if (applied)
            await RecomputeAverageAsync(productId, cancellationToken);
    }

    // Returns false (no-op) when a ConditionalCheckFailedException indicates this eventId was
    // already applied — an idempotent replay, not an error.
    private async Task<bool> TryApplyRatingDeltaAsync(
        string productId, string eventId, int ratingSumDelta, int ratingCountDelta,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
                    UpdateExpression = "ADD RatingSum :ratingSumDelta, RatingCount :ratingCountDelta " +
                                        "SET LastRatingEventId = :eventId",
                    ConditionExpression =
                        "attribute_not_exists(LastRatingEventId) OR LastRatingEventId <> :eventId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":ratingSumDelta"] = new AttributeValue
                        {
                            N = ratingSumDelta.ToString(CultureInfo.InvariantCulture)
                        },
                        [":ratingCountDelta"] = new AttributeValue
                        {
                            N = ratingCountDelta.ToString(CultureInfo.InvariantCulture)
                        },
                        [":eventId"] = new(eventId)
                    }
                },
                cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private async Task RecomputeAverageAsync(string productId, CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) }
            },
            cancellationToken);

        if (response.Item is not { Count: > 0 })
            return;

        var sum = response.Item.TryGetValue("RatingSum", out var sumAttr) && !string.IsNullOrEmpty(sumAttr.N)
            ? int.Parse(sumAttr.N, CultureInfo.InvariantCulture)
            : 0;
        var count = response.Item.TryGetValue("RatingCount", out var countAttr) && !string.IsNullOrEmpty(countAttr.N)
            ? int.Parse(countAttr.N, CultureInfo.InvariantCulture)
            : 0;

        var average = count == 0 ? 0 : (double)sum / count;

        await dynamoDb.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
                UpdateExpression = "SET AverageRating = :average, GSI1SK = :average",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":average"] = new AttributeValue { N = average.ToString(CultureInfo.InvariantCulture) }
                }
            },
            cancellationToken);
    }

    public Task ApplyPricingAsync(
        string productId,
        decimal originalPrice,
        decimal price,
        decimal cashPrice,
        int maxInstallmentsWithoutInterest,
        decimal maxInstallmentValue,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) },
            UpdateExpression = "SET OriginalPrice = :originalPrice, Price = :price, CashPrice = :cashPrice, " +
                                "MaxInstallmentsWithoutInterest = :maxInstallments, " +
                                "MaxInstallmentValue = :maxInstallmentValue",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":originalPrice"] = new AttributeValue { N = originalPrice.ToString(CultureInfo.InvariantCulture) },
                [":price"] = new AttributeValue { N = price.ToString(CultureInfo.InvariantCulture) },
                [":cashPrice"] = new AttributeValue { N = cashPrice.ToString(CultureInfo.InvariantCulture) },
                [":maxInstallments"] = new AttributeValue
                {
                    N = maxInstallmentsWithoutInterest.ToString(CultureInfo.InvariantCulture)
                },
                [":maxInstallmentValue"] = new AttributeValue
                {
                    N = maxInstallmentValue.ToString(CultureInfo.InvariantCulture)
                }
            }
        };

        return dynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task<SearchDocument?> GetAsync(string productId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(productId) }
            },
            cancellationToken);

        return response.Item is not { Count: > 0 } ? null : FromItem(response.Item);
    }

    public async Task BulkIndexAsync(
        IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        const int batchSize = 25;

        foreach (var chunk in documents.Chunk(batchSize))
        {
            if (chunk.Length == 0)
                continue;

            var request = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [TableName] = [.. chunk.Select(document => new WriteRequest
                    {
                        PutRequest = new PutRequest { Item = ToItem(document) }
                    })]
                }
            };

            await dynamoDb.BatchWriteItemAsync(request, cancellationToken);
        }
    }

    private static AttributeValue ToStringList(IEnumerable<string> values) =>
        new() { L = [.. values.Select(v => new AttributeValue(v))] };

    private static AttributeValue ToImageList(IEnumerable<ImageRef> images) =>
        new()
        {
            L =
            [
                .. images.Select(image => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["ImageId"] = new(image.ImageId),
                        ["IsMain"] = new AttributeValue { BOOL = image.IsMain },
                        ["Order"] = new AttributeValue { N = image.Order.ToString(CultureInfo.InvariantCulture) }
                    }
                })
            ],
            IsLSet = true
        };

    private static AttributeValue ToCategoryList(IEnumerable<CategoryRef> categories) =>
        new()
        {
            L =
            [
                .. categories.Select(category => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = new(category.Id),
                        ["Name"] = new(category.Name)
                    }
                })
            ]
        };

    // Full-document write, used only by BulkIndexAsync (the one-time seeder backfill).
    private static Dictionary<string, AttributeValue> ToItem(SearchDocument document)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(document.Id),
            ["Name"] = new(document.Name),
            ["Description"] = new(document.Description),
            ["Images"] = ToImageList(document.Images),
            ["OriginalPrice"] = new AttributeValue { N = document.OriginalPrice.ToString(CultureInfo.InvariantCulture) },
            ["Stock"] = new AttributeValue { N = document.Stock.ToString(CultureInfo.InvariantCulture) },
            ["CategoryIds"] = ToStringList(document.CategoryIds),
            ["Categories"] = ToCategoryList(document.Categories),
            ["AverageRating"] = new AttributeValue { N = document.AverageRating.ToString(CultureInfo.InvariantCulture) },
            ["RatingCount"] = new AttributeValue { N = document.RatingCount.ToString(CultureInfo.InvariantCulture) },
            ["Price"] = new AttributeValue { N = document.Price.ToString(CultureInfo.InvariantCulture) },
            ["CashPrice"] = new AttributeValue { N = document.CashPrice.ToString(CultureInfo.InvariantCulture) },
            ["MaxInstallmentsWithoutInterest"] = new AttributeValue
            {
                N = document.MaxInstallmentsWithoutInterest.ToString(CultureInfo.InvariantCulture)
            },
            ["MaxInstallmentValue"] = new AttributeValue
            {
                N = document.MaxInstallmentValue.ToString(CultureInfo.InvariantCulture)
            },
            ["RatingSum"] = new AttributeValue { N = document.RatingSum.ToString(CultureInfo.InvariantCulture) },
            ["GSI1PK"] = new(Gsi1PartitionValue),
            ["GSI1SK"] = new AttributeValue { N = document.AverageRating.ToString(CultureInfo.InvariantCulture) }
        };

        if (!string.IsNullOrEmpty(document.LastRatingEventId))
            item["LastRatingEventId"] = new AttributeValue(document.LastRatingEventId);

        return item;
    }

    private static SearchDocument FromItem(Dictionary<string, AttributeValue> item) =>
        new()
        {
            Id = item.TryGetValue("Id", out var id) ? id.S : string.Empty,
            Name = item.TryGetValue("Name", out var name) ? name.S : string.Empty,
            Description = item.TryGetValue("Description", out var description) ? description.S : string.Empty,
            Images = item.TryGetValue("Images", out var images) && images.L is not null
                ? [.. images.L.Select(ToImageRef)]
                : [],
            OriginalPrice = ParseDecimal(item, "OriginalPrice"),
            Stock = ParseInt(item, "Stock"),
            CategoryIds = item.TryGetValue("CategoryIds", out var categoryIds) && categoryIds.L is not null
                ? [.. categoryIds.L.Select(v => v.S)]
                : [],
            Categories = item.TryGetValue("Categories", out var categories) && categories.L is not null
                ? [.. categories.L.Select(c => new CategoryRef(c.M["Id"].S, c.M["Name"].S))]
                : [],
            AverageRating = item.TryGetValue("AverageRating", out var averageRating) && !string.IsNullOrEmpty(averageRating.N)
                ? double.Parse(averageRating.N, CultureInfo.InvariantCulture)
                : 0,
            RatingCount = ParseInt(item, "RatingCount"),
            Price = ParseDecimal(item, "Price"),
            CashPrice = ParseDecimal(item, "CashPrice"),
            MaxInstallmentsWithoutInterest = ParseInt(item, "MaxInstallmentsWithoutInterest"),
            MaxInstallmentValue = ParseDecimal(item, "MaxInstallmentValue"),
            RatingSum = ParseInt(item, "RatingSum"),
            LastRatingEventId = item.TryGetValue("LastRatingEventId", out var lastRatingEventId)
                ? lastRatingEventId.S
                : null
        };

    private static ImageRef ToImageRef(AttributeValue value) =>
        new(
            value.M.TryGetValue("ImageId", out var imageId) ? imageId.S : string.Empty,
            value.M.TryGetValue("IsMain", out var isMain) && isMain.BOOL == true,
            value.M.TryGetValue("Order", out var order) && !string.IsNullOrEmpty(order.N)
                ? int.Parse(order.N, CultureInfo.InvariantCulture)
                : 0);

    private static int ParseInt(Dictionary<string, AttributeValue> item, string attributeName) =>
        item.TryGetValue(attributeName, out var attr) && !string.IsNullOrEmpty(attr.N)
            ? int.Parse(attr.N, CultureInfo.InvariantCulture)
            : 0;

    private static decimal ParseDecimal(Dictionary<string, AttributeValue> item, string attributeName) =>
        item.TryGetValue(attributeName, out var attr) && !string.IsNullOrEmpty(attr.N)
            ? decimal.Parse(attr.N, CultureInfo.InvariantCulture)
            : 0m;
}
