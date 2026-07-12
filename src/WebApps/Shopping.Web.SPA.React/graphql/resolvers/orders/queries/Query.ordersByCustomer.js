import { util } from '@aws-appsync/utils'

// AppSync direct DynamoDB resolver (ADR-0009): Query the ordering GSI1 by customer, newest-first.
// Always uses the authenticated user's sub — never trusts a client-supplied customerId.
export function request(ctx) {
  const req = {
    operation: 'Query',
    index: 'GSI1',
    query: {
      expression: 'GSI1PK = :pk',
      expressionValues: util.dynamodb.toMapValues({ ':pk': `CUSTOMER#${ctx.identity.sub}` }),
    },
    scanIndexForward: false,
  }
  if (ctx.args.nextToken) req.nextToken = ctx.args.nextToken
  return req
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: (ctx.result.items ?? []).map(item => ({
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
