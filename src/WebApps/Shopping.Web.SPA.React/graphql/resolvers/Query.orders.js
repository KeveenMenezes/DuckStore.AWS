import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const { pageSize = 10, nextToken } = ctx.args
  return {
    operation: 'Scan',
    filter: {
      expression: '#Type = :type',
      expressionNames: { '#Type': 'Type' },
      expressionValues: { ':type': util.dynamodb.toDynamoDB('Order') },
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
      },
    })),
    nextToken: ctx.result.nextToken ?? null,
  }
}
