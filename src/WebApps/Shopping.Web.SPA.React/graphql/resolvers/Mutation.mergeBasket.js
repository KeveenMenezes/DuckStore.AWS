import { util } from '@aws-appsync/utils'

// Cognito-only: called by the BFF right after login. Derives the user identity from the token
// and receives the guest identity (GUEST#<guestId>) that the BFF injected from the guest cookie.
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  const guestId = ctx.args.guestId
  if (!guestId || !guestId.startsWith('GUEST#')) util.unauthorized()

  return {
    operation: 'Invoke',
    payload: {
      OwnerId: `USER#${ctx.identity.sub}`,
      GuestId: guestId,
    },
  }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return { ownerId: ctx.result.OwnerId }
}
