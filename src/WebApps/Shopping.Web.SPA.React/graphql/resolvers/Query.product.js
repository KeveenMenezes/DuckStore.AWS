import { util } from '@aws-appsync/utils'

// AppSync Lambda resolver — invokes CatalogView's GetProduct (ADR-0027 amends ADR-0009: OpenSearch
// is an external, non-DynamoDB integration). Price moved to Pricing (ADR-0026) but is denormalized
// onto CatalogView's search document via CDC (ADR-0027), along with the payment badge (ADR-0028).
export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { Id: ctx.args.id },
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
