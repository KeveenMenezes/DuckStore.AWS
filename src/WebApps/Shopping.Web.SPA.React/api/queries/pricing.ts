// Detailed per-installment breakdown, computed synchronously by Pricing on every call — never
// denormalized onto Product (see fragments.ts). Fetched by the product detail page only.
export const GET_INSTALLMENT_PLAN = `
  query GetInstallmentPlan($productId: ID!) {
    installmentPlanFor(productId: $productId) {
      productId
      originalPrice
      price
      cashPrice
      maxInstallmentsWithoutInterest
      installments { count value totalValue hasInterest }
    }
  }
`

// Cart-level equivalent: sums cost/originalPrice across every item and returns one unified plan
// for the whole checkout transaction. Fetched by the checkout page. discountId is optional and
// Cognito-only (ADR-0046 §5) — see getBasketInstallmentPlan, which switches auth clients when set.
export const GET_BASKET_INSTALLMENT_PLAN = `
  query GetBasketInstallmentPlan($items: [BasketInstallmentItemInput!]!, $discountId: ID) {
    basketInstallmentPlan(items: $items, discountId: $discountId) {
      totalOriginalPrice
      price
      cashPrice
      maxInstallmentsWithoutInterest
      installments { count value totalValue hasInterest }
    }
  }
`

// NOT WIRED UP YET — the two queries below, and the optional `discountId` on
// getBasketInstallmentPlan/submitCheckout, are the client half of CH-13 (docs/backlog/challenges).
// The server side is done; the cart's apply/remove-coupon UI is not, so nothing calls these.

// A customer-scoped reward available to the signed-in customer (ADR-0046 §4). Cognito-only.
export const GET_MY_REWARDS = `
  query MyRewards {
    myRewards {
      id
      amount
      status
      expiresAt
      sourceRedemptionId
    }
  }
`

// The current points -> currency rate, so the SPA can show "500 points -> R$50" before redemption
// (ADR-0046 §1, §8). Public within Cognito (no anonymous quote — redemption itself never is).
export const GET_REWARD_CONVERSION = `
  query RewardConversion {
    rewardConversion {
      pointsPerUnit
      currencyPerUnit
      expiryDays
    }
  }
`
