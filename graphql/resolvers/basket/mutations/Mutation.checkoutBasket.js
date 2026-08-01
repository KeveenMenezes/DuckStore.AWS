import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      BasketCheckoutDto: {
        OwnerId: `USER#${ctx.identity.sub}`,  // Cognito-only mutation — derive owner from the token
        CustomerId: ctx.identity.sub,  // always from Cognito — never trust client value
        TotalPrice: input.totalPrice,
        // Opaque pass-through — never interpreted here (ADR-0046 §6).
        DiscountId: input.discountId ?? null,
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
  return { isSuccess: ctx.result.IsSuccess }
}
