// v1 conversion is a fixed constant, not a tier catalogue (ADR-0046 §8). Single source of truth
// for both pricing-points-redeemed-consumer's Rewards__* env vars (pricing-lambdas.ts) and the
// rewardConversion NONE resolver's baked-in values (appsync-api.ts) — a NONE resolver has no
// runtime config to read, so it must match the Lambda's env exactly or the SPA's quote would lie.
export const REWARD_POINTS_PER_UNIT = 100;
export const REWARD_CURRENCY_PER_UNIT = 10;
export const REWARD_EXPIRY_DAYS = 90;
