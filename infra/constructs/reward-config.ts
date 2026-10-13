// v1 conversion is a fixed constant, not a tier catalogue (ADR-0048 §3). Single source of truth
// for the Rewards__* env vars of the Pricing Lambdas that convert points (pricing-lambdas.ts) and
// the rewardConversion NONE resolver's baked-in values (appsync-api.ts) — a NONE resolver has no
// runtime config to read, so it must match the Lambda's env exactly or the SPA's quote would lie.
//
// Business rules (ADR-0048 §3): 100 points = R$1, redeemable up to 20% of the
// post-campaign cart price.
export const REWARD_POINTS_PER_UNIT = 100;
export const REWARD_CURRENCY_PER_UNIT = 1;
export const REWARD_MAX_DISCOUNT_PERCENT = 20;
