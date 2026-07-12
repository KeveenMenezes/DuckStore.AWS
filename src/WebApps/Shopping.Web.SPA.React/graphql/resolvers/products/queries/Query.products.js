import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver on CatalogView's "catalogview-products" table (ADR-0030,
// supersedes ADR-0027's OpenSearch/Lambda design). Price moved to Pricing (ADR-0026) but is
// denormalized onto this table via CDC, along with the payment badge (ADR-0028).
//
// No full-text/relevance search: `query` only does a substring `contains()` filter on name/
// description, which DynamoDB cannot express as a key condition — that branch stays a Scan (same
// accepted tradeoff as ADR-0030 "Negative/Costs"; a real search index was already ruled out on
// cost grounds). But the far more common case — browsing/sorting with no free-text `query` — has
// no such requirement, so it Queries GSI1 (GSI1PK constant "PRODUCT", GSI1SK = AverageRating)
// instead: minRating/maxRating become a native sort-key range instead of a post-read
// FilterExpression, sorting by rating is native (scanIndexForward) instead of a per-page
// re-sort, and RCU cost no longer scales with total catalog size.
export function request(ctx) {
  const { query, minRating, maxRating, sortBy, pageSize, nextToken } = ctx.args

  if (query) {
    return {
      operation: 'Scan',
      limit: pageSize ?? 20,
      nextToken,
      filter: {
        expression: '(contains(#Name, :q) OR contains(#Description, :q)) AND #AverageRating BETWEEN :min AND :max',
        expressionNames: { '#Name': 'Name', '#Description': 'Description', '#AverageRating': 'AverageRating' },
        expressionValues: {
          ':q': util.dynamodb.toDynamoDB(query),
          ':min': util.dynamodb.toDynamoDB(minRating ?? 0),
          ':max': util.dynamodb.toDynamoDB(maxRating ?? 5),
        },
      },
    }
  }

  return {
    operation: 'Query',
    index: 'GSI1',
    query: {
      expression: 'GSI1PK = :pk AND GSI1SK BETWEEN :min AND :max',
      expressionValues: {
        ':pk': util.dynamodb.toDynamoDB('PRODUCT'),
        ':min': util.dynamodb.toDynamoDB(minRating ?? 0),
        ':max': util.dynamodb.toDynamoDB(maxRating ?? 5),
      },
    },
    scanIndexForward: sortBy !== 'AVERAGE_RATING',
    limit: pageSize ?? 20,
    nextToken,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = (ctx.result.items ?? []).map(item => ({
    id: item.Id,
    name: item.Name,
    description: item.Description,
    imageUrl: item.ImageUrl,
    stock: item.Stock,
    categoryIds: item.CategoryIds ?? [],
    averageRating: item.AverageRating ?? 0,
    ratingCount: item.RatingCount ?? 0,
    originalPrice: item.OriginalPrice ?? 0,
    price: item.Price ?? 0,
    cashPrice: item.CashPrice ?? 0,
    maxInstallmentsWithoutInterest: item.MaxInstallmentsWithoutInterest ?? 0,
    maxInstallmentValue: item.MaxInstallmentValue ?? 0,
  }))

  // Only the free-text Scan branch still needs a manual sort: it has no native ORDER BY, and
  // even this is best-effort — it only orders the current page, not the full result set across
  // pages (ADR-0030). The GSI1 Query branch above sorts natively via scanIndexForward.
  //
  // No comparator passed to Array.sort — the AppSync JS runtime rejects functions passed as
  // arguments to anything but a handful of allow-listed methods (map/filter/forEach/etc.),
  // and sort's comparator isn't one of them ("The code contains one or more errors" at deploy
  // time). Insertion sort descending by averageRating instead.
  if (ctx.args.query && ctx.args.sortBy === 'AVERAGE_RATING') {
    for (let i = 1; i < items.length; i++) {
      const current = items[i]
      let j = i - 1
      while (j >= 0 && items[j].averageRating < current.averageRating) {
        items[j + 1] = items[j]
        j--
      }
      items[j + 1] = current
    }
  }

  return { items, nextToken: ctx.result.nextToken ?? null }
}
