import { util } from '@aws-appsync/utils'

// AppSync Lambda resolver — invokes CatalogView's SearchProducts (ADR-0027 amends ADR-0009:
// OpenSearch is an external, non-DynamoDB integration). Full-text search, rating range filter and
// sort now live in CatalogView, not Catalog's DynamoDB table. Price moved to Pricing (ADR-0026) but
// is denormalized onto CatalogView's search document via CDC, along with the payment badge (ADR-0028).
export function request(ctx) {
  const { query, sortBy, minRating, maxRating, pageSize, nextToken } = ctx.args
  return {
    operation: 'Invoke',
    payload: {
      Query: query ?? null,
      SortBy: sortBy ?? null,
      MinRating: minRating ?? null,
      MaxRating: maxRating ?? null,
      PageSize: pageSize ?? 20,
      NextToken: nextToken ?? null,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const items = (ctx.result.Items ?? []).map(item => ({
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

  return { items, nextToken: ctx.result.NextToken ?? null }
}
