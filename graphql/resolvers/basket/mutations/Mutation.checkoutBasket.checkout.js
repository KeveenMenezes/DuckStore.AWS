import { util } from '@aws-appsync/utils'

// Pipeline step 3 of checkoutBasket: the Basket Lambda checkout, with the total Pricing confirmed
// in step 2 — the client's totalPrice never goes further than that comparison (ADR-0048 §4).
// PointsToUse is passed through untouched; Basket never interprets it (ADR-0026).
export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      BasketCheckoutDto: {
        OwnerId: `USER#${ctx.identity.sub}`,  // Cognito-only mutation — derive owner from the token
        CustomerId: ctx.identity.sub,  // always from Cognito — never trust client value
        TotalPrice: ctx.stash.chargedTotal,
        PointsToUse: input.pointsToUse ?? 0,
        ShippingAddress: {
          FirstName: input.firstName,
          LastName: input.lastName,
          EmailAddress: input.emailAddress,
          AddressLine: input.addressLine,
          Country: input.country,
          State: input.state,
          ZipCode: input.zipCode,
        },
        // Optional — empty for Cash, which carries no card (BasketCheckoutDto's fields are
        // plain non-nullable strings, so default explicitly rather than passing null through).
        Payment: {
          CardName: input.cardName ?? '',
          CardNumber: input.cardNumber ?? '',
          Expiration: input.expiration ?? '',
          Cvv: input.cvv ?? '',
          PaymentMethod: input.paymentMethod,
          Installments: input.installments,
        },
      },
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: ctx.result.IsSuccess, orderId: ctx.result.OrderId ?? null }
}
