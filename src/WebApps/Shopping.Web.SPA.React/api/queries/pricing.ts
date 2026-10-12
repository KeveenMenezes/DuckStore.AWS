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
// for the whole checkout transaction. Fetched by the checkout page. A positive pointsToUse is
// Cognito-only (ADR-0048 §3) — see getBasketInstallmentPlan, which switches auth clients when it
// is set.
export const GET_BASKET_INSTALLMENT_PLAN = `
  query GetBasketInstallmentPlan($items: [BasketInstallmentItemInput!]!, $pointsToUse: Int) {
    basketInstallmentPlan(items: $items, pointsToUse: $pointsToUse) {
      totalOriginalPrice
      price
      cashPrice
      maxInstallmentsWithoutInterest
      installments { count value totalValue hasInterest }
      maxRedeemablePoints
      pointsDiscount
    }
  }
`

// The current points -> currency rate, so the SPA can show "500 points = R$5" before the customer
// uses them (ADR-0046 §1, ADR-0048 §3). Cognito-only, like using points.
export const GET_REWARD_CONVERSION = `
  query RewardConversion {
    rewardConversion {
      pointsPerUnit
      currencyPerUnit
    }
  }
`
