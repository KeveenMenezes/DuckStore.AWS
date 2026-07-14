import { util } from '@aws-appsync/utils'

// Image invariants (ADR-0034): valid ULID keys, at most 12, exactly one main. Enforced at
// the write edge — the imageIds are client-echoed values from createProductImageUpload.
function validateImages(images) {
  if (images.length > 12) util.error('A product can have at most 12 images.', 'BadRequest')
  const mainCount = images.filter(i => i.isMain).length
  if (images.length > 0 && mainCount !== 1) {
    util.error('Exactly one image must be marked as main.', 'BadRequest')
  }
  images.forEach(i => {
    if (!util.matches('^[0-9A-HJKMNP-TV-Z]{26}$', i.imageId)) {
      util.error(`Invalid imageId "${i.imageId}".`, 'BadRequest')
    }
  })
}

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  const { name, description, images, stock, categoryIds } = ctx.args.input
  validateImages(images)
  const id = util.autoId()
  ctx.stash.id = id
  // Price is set separately via Pricing's setNominalPrice mutation (ADR-0026) — Catalog no
  // longer stores it. Images are metadata only (ADR-0034) — keys, never URLs.
  return {
    operation: 'PutItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    attributeValues: {
      Name: util.dynamodb.toDynamoDB(name),
      Description: util.dynamodb.toDynamoDB(description),
      Images: util.dynamodb.toDynamoDB(
        images.map(i => ({ ImageId: i.imageId, IsMain: i.isMain, Order: i.order })),
      ),
      Stock: util.dynamodb.toDynamoDB(stock),
      CategoryIds: util.dynamodb.toStringSet(categoryIds),
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { id: ctx.stash.id }
}
