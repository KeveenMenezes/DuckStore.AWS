import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin') && !groups.includes('Seller')) util.unauthorized()

  const { id, name, description, imageUrl, stock, categoryIds } = ctx.args.input
  // Price is updated separately via Pricing's setNominalPrice mutation (ADR-0026).
  return {
    operation: 'UpdateItem',
    key: { Id: util.dynamodb.toDynamoDB(id) },
    update: {
      expression: 'SET #Name = :name, Description = :desc, ImageUrl = :img, Stock = :stock, CategoryIds = :cats',
      expressionNames: { '#Name': 'Name' },
      expressionValues: {
        ':name': util.dynamodb.toDynamoDB(name),
        ':desc': util.dynamodb.toDynamoDB(description),
        ':img': util.dynamodb.toDynamoDB(imageUrl),
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
