import { util } from '@aws-appsync/utils'

// AppSync NONE (local) resolver — no backend call. Values are baked in at synth time by
// appsync-api.ts from the same constants pricing-points-redeemed-consumer's Rewards__* env vars
// use (infra/constructs/reward-config.ts) — a NONE resolver has no runtime config to read
// (ADR-0046 §8).
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return { payload: {} }
}

export function response(ctx) {
  if (ctx.error) util.error(ctx.error.message, ctx.error.type)
  return {
    pointsPerUnit: __REWARD_POINTS_PER_UNIT__,
    currencyPerUnit: __REWARD_CURRENCY_PER_UNIT__,
    expiryDays: __REWARD_EXPIRY_DAYS__,
  }
}
