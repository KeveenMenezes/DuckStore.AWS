import { util } from "@aws-appsync/utils";

// AppSync direct DynamoDB resolver on CatalogView's "catalogview-products" table (ADR-0030,
// supersedes ADR-0027's OpenSearch/Lambda design). Price moved to Pricing (ADR-0026) but is
// denormalized onto this table via CDC, along with the payment badge (ADR-0028).
export function request(ctx) {
  const { query, minRating, maxRating, sortBy, pageSize, nextToken } = ctx.args;

  if (query) {
    return {
      operation: "Scan",
      limit: pageSize ?? 20,
      nextToken,
      filter: {
        expression:
          "(contains(#Name, :q) OR contains(#Description, :q)) AND #AverageRating BETWEEN :min AND :max",
        expressionNames: {
          "#Name": "Name",
          "#Description": "Description",
          "#AverageRating": "AverageRating",
        },
        expressionValues: {
          ":q": util.dynamodb.toDynamoDB(query),
          ":min": util.dynamodb.toDynamoDB(minRating ?? 0),
          ":max": util.dynamodb.toDynamoDB(maxRating ?? 5),
        },
      },
    };
  }

  return {
    operation: "Query",
    index: "GSI1",
    query: {
      expression: "GSI1PK = :pk AND GSI1SK BETWEEN :min AND :max",
      expressionValues: {
        ":pk": util.dynamodb.toDynamoDB("PRODUCT"),
        ":min": util.dynamodb.toDynamoDB(minRating ?? 0),
        ":max": util.dynamodb.toDynamoDB(maxRating ?? 5),
      },
    },
    scanIndexForward: sortBy !== "AVERAGE_RATING",
    limit: pageSize ?? 20,
    nextToken,
  };
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type);

  const items = (ctx.result.items ?? []).map((item) => ({
    id: item.Id,
    name: item.Name,
    description: item.Description,
    // Metadata only (ADR-0034); empty for products created before the image pipeline.
    images: (item.Images ?? []).map((img) => ({
      imageId: img.ImageId,
      isMain: img.IsMain,
      order: img.Order,
    })),
    stock: item.Stock,
    categoryIds: item.CategoryIds ?? [],
    averageRating: item.AverageRating ?? 0,
    ratingCount: item.RatingCount ?? 0,
    originalPrice: item.OriginalPrice ?? 0,
    price: item.Price ?? 0,
    cashPrice: item.CashPrice ?? 0,
    maxInstallmentsWithoutInterest: item.MaxInstallmentsWithoutInterest ?? 0,
    maxInstallmentValue: item.MaxInstallmentValue ?? 0,
  }));

  // Only the free-text Scan branch still needs a manual sort: it has no native ORDER BY, and
  // even this is best-effort — it only orders the current page, not the full result set across
  // pages (ADR-0030). The GSI1 Query branch above sorts natively via scanIndexForward.
  //
  // The AppSync JS runtime rejects a comparator passed to Array.sort (functions may only be
  // passed to allow-listed array methods like map/filter/forEach), and also rejects while /
  // classic for(;;) loops and ++/-- — all "The code contains one or more errors" at deploy
  // time. So: decorate each item with a fixed-width string key whose lexicographic order is
  // descending averageRating (rating inverted against the 0–5 scale), sort() with no
  // arguments, and map the sorted keys back to items via the appended unique index.
  if (ctx.args.query && ctx.args.sortBy === "AVERAGE_RATING") {
    const keys = items.map(
      (item, index) =>
        `${("000000" + Math.round((5 - (item.averageRating ?? 0)) * 100000)).slice(-6)}|${index}`,
    );
    const sortedKeys = keys.slice();
    sortedKeys.sort();
    return {
      items: sortedKeys.map((key) => items[keys.indexOf(key)]),
      nextToken: ctx.result.nextToken ?? null,
    };
  }

  return { items, nextToken: ctx.result.nextToken ?? null };
}
