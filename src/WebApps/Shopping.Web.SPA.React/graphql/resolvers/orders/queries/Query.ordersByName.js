import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const groups = ctx.identity?.groups ?? []
  if (!groups.includes('Admin')) util.unauthorized()

  const { name, pageSize = 10, nextToken } = ctx.args
  return {
    operation: 'Scan',
    filter: {
      expression: 'contains(OrderName, :name) AND #Type = :type',
      expressionNames: { '#Type': 'Type' },
      expressionValues: {
        ':name': util.dynamodb.toDynamoDB(name),
        ':type': util.dynamodb.toDynamoDB('Order'),
      },
    },
    limit: pageSize,
    nextToken,
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: ctx.result.items.map(item => ({
      id: item.Id,
      customerId: item.CustomerId,
      orderName: item.OrderName,
      status: item.Status,
      createdAt: item.CreatedAt ?? null,
      shippingAddress: {
        firstName: item.ShippingAddress?.FirstName ?? '',
        lastName: item.ShippingAddress?.LastName ?? '',
        emailAddress: item.ShippingAddress?.EmailAddress ?? '',
        addressLine: item.ShippingAddress?.AddressLine ?? '',
        country: item.ShippingAddress?.Country ?? '',
        state: item.ShippingAddress?.State ?? '',
        zipCode: item.ShippingAddress?.ZipCode ?? '',
      },
      payment: {
        cardName: item.Payment?.CardName ?? '',
        cardNumber: item.Payment?.CardNumber ?? '',
        expiration: item.Payment?.Expiration ?? '',
        cvv: item.Payment?.Cvv ?? '',
        paymentMethod: item.Payment?.PaymentMethod ?? 0,
        installments: item.Payment?.Installments ?? 1,
      },
    })),
    nextToken: ctx.result.nextToken ?? null,
  }
}
