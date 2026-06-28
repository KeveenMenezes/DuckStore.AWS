import { util } from '@aws-appsync/utils'

export function request(ctx) {
  return {
    operation: 'Invoke',
    payload: { CustomerId: ctx.args.customerId },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    items: (ctx.result.Orders ?? []).map(o => ({
      id: String(o.Id),
      customerId: String(o.CustomerId),
      orderName: o.OrderName,
      status: String(o.Status),
      createdAt: null,
      shippingAddress: {
        firstName: o.ShippingAddress?.FirstName ?? '',
        lastName: o.ShippingAddress?.LastName ?? '',
        emailAddress: o.ShippingAddress?.EmailAddress ?? '',
        addressLine: o.ShippingAddress?.AddressLine ?? '',
        country: o.ShippingAddress?.Country ?? '',
        state: o.ShippingAddress?.State ?? '',
        zipCode: o.ShippingAddress?.ZipCode ?? '',
      },
      payment: {
        cardName: o.Payment?.CardName ?? '',
        cardNumber: o.Payment?.CardNumber ?? '',
        expiration: o.Payment?.Expiration ?? '',
        cvv: o.Payment?.Cvv ?? '',
        paymentMethod: o.Payment?.PaymentMethod ?? 0,
      },
    })),
    nextToken: null,
  }
}
