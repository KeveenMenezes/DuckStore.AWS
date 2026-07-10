import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — Scan + filter on CatalogView's "catalogview-products" table
// (ADR-0030, supersedes ADR-0027's OpenSearch/Lambda design), same pattern as
// Query.orders.js/Query.categories.js. Price moved to Pricing (ADR-0026) but is denormalized onto
// this table via CDC, along with the payment badge (ADR-0028).
//
// No full-text/relevance search: `query` only does a substring `contains()` filter on name/
// description (Scan has no relevance ranking). `minRating`/`maxRating` filter on AverageRating.
export function request(ctx) {
  const { query, minRating, maxRating, pageSize, nextToken } = ctx.args

  const expressionNames = {}
  const expressionValues = {}
  const clauses = []

  if (query) {
    expressionNames['#Name'] = 'Name'
    expressionNames['#Description'] = 'Description'
    expressionValues[':q'] = util.dynamodb.toDynamoDB(query)
    clauses.push('(contains(#Name, :q) OR contains(#Description, :q))')
  }

  if (minRating != null || maxRating != null) {
    expressionNames['#AverageRating'] = 'AverageRating'
    expressionValues[':min'] = util.dynamodb.toDynamoDB(minRating ?? 0)
    expressionValues[':max'] = util.dynamodb.toDynamoDB(maxRating ?? 5)
    clauses.push('#AverageRating BETWEEN :min AND :max')
  }

  const request = {
    operation: 'Scan',
    limit: pageSize ?? 20,
    nextToken,
  }

  if (clauses.length > 0) {
    request.filter = {
      expression: clauses.join(' AND '),
      expressionNames,
      expressionValues,
    }
  }

  return request
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

  // Best-effort sort: DynamoDB Scan has no ORDER BY, so this only orders the current page of
  // items just returned — it is not a globally sorted result across pages (ADR-0030).
  if (ctx.args.sortBy === 'AVERAGE_RATING') {
    items.sort((a, b) => b.averageRating - a.averageRating)
  }

  return { items, nextToken: ctx.result.nextToken ?? null }
}
