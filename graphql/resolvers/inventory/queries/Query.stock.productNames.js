import { util, runtime } from '@aws-appsync/utils'

// Pipeline step on Catalog's products table (read only), shared by the two warehouse views
// (ADR-0052 §7). It only decorates rows with display names — a deleted product, or a key DynamoDB left
// unprocessed, gets productName null — so it stays a direct step (ADR-0009). BatchGetItem rejects
// duplicate keys, hence the de-duplication; an empty page skips the call.
export function request(ctx) {
  const page = ctx.prev.result
  const ids = []
  for (const item of page.items) {
    if (!ids.includes(item.productId)) ids.push(item.productId)
  }
  if (ids.length === 0) return runtime.earlyReturn(page)

  return {
    operation: 'BatchGetItem',
    tables: {
      products: {
        keys: ids.map((id) => util.dynamodb.toMapValues({ Id: id })),
      },
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)

  const names = {}
  for (const product of ctx.result.data.products ?? []) {
    if (product) names[product.Id] = product.Name
  }
  const page = ctx.prev.result
  return {
    items: page.items.map((item) => ({ ...item, productName: names[item.productId] ?? null })),
    nextToken: page.nextToken,
  }
}
