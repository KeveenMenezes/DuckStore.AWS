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
  // An item without a Name is not a product yet: Inventory's total (ADR-0052 §8) or a price reached
  // CatalogView before the product's ProductSynced.
  if (!ctx.result || !ctx.result.Name) return null

  const item = ctx.result
  return {
    id: item.Id,
    name: item.Name,
    description: item.Description,
    // Metadata only (ADR-0034); empty for products created before the image pipeline.
    images: (item.Images ?? []).map(img => ({
      imageId: img.ImageId,
      isMain: img.IsMain,
      order: img.Order,
    })),
    // From Inventory only (ADR-0052 §8): 0 until the product's first total arrives.
    stock: item.Stock ?? 0,
    categoryIds: item.CategoryIds ?? [],
    averageRating: item.AverageRating ?? 0,
    ratingCount: item.RatingCount ?? 0,
    ratingDistribution: item.RatingDistribution ?? {},
    originalPrice: item.OriginalPrice ?? 0,
    price: item.Price ?? 0,
    cashPrice: item.CashPrice ?? 0,
    maxInstallmentsWithoutInterest: item.MaxInstallmentsWithoutInterest ?? 0,
    maxInstallmentValue: item.MaxInstallmentValue ?? 0,
  }
}
