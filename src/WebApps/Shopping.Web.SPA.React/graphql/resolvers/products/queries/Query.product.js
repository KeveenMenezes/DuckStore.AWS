import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver — GetItem on CatalogView's "catalogview-products" table
// (ADR-0030, supersedes ADR-0027's OpenSearch/Lambda design). Price moved to Pricing (ADR-0026)
// but is denormalized onto this table via CDC, along with the payment badge (ADR-0028).
export function request(ctx) {
  return {
    operation: 'GetItem',
    key: { Id: util.dynamodb.toDynamoDB(ctx.args.id) },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  if (!ctx.result) return null

  const item = ctx.result
  return {
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
  }
}
