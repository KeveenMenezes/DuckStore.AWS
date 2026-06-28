import { util } from '@aws-appsync/utils'

export function request(ctx) {
  const input = ctx.args.input
  return {
    operation: 'Invoke',
    payload: {
      BasketCheckoutDto: {
        UserName: input.userName,
        CustomerId: input.customerId,
        TotalPrice: input.totalPrice,
        FirstName: input.firstName,
        LastName: input.lastName,
        EmailAddress: input.emailAddress,
        AddressLine: input.addressLine,
        Country: input.country,
        State: input.state,
        ZipCode: input.zipCode,
        CardName: input.cardName,
        CardNumber: input.cardNumber,
        Expiration: input.expiration,
        Cvv: input.cvv,
        PaymentMethod: input.paymentMethod,
      },
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { isSuccess: ctx.result.IsSuccess }
}
