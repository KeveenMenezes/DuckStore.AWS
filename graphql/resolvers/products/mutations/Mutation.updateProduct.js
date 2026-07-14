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

  const { id, name, description, images, stock, categoryIds } = ctx.args.input
  validateImages(images)
  // Price is updated separately via Pricing's setNominalPrice mutation (ADR-0026).
  return {
    operation: 'UpdateItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    update: {
      expression: 'SET #Name = :name, Description = :desc, Images = :imgs, Stock = :stock, CategoryIds = :cats',
      expressionNames: { '#Name': 'Name' },
      expressionValues: {
        ':name': util.dynamodb.toDynamoDB(name),
        ':desc': util.dynamodb.toDynamoDB(description),
        ':imgs': util.dynamodb.toDynamoDB(
          images.map(i => ({ ImageId: i.imageId, IsMain: i.isMain, Order: i.order })),
        ),
        ':stock': util.dynamodb.toDynamoDB(stock),
        ':cats': util.dynamodb.toStringSet(categoryIds),
      },
    },
    condition: {
      expression: 'attribute_exists(Id)',
    },
  }
}

export function response(ctx) {
  if (ctx.error) {
    if (ctx.error.type === 'DynamoDB:ConditionalCheckFailedException') {
      util.error('Product not found', 'NOT_FOUND')
    }
    util.error(ctx.error.message, ctx.error.type)
  }
  return { id: ctx.args.input.id }
}
