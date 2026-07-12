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
// for the whole checkout transaction. Fetched by the checkout page.
export const GET_BASKET_INSTALLMENT_PLAN = `
  query GetBasketInstallmentPlan($items: [BasketInstallmentItemInput!]!) {
    basketInstallmentPlan(items: $items) {
      totalOriginalPrice
      price
      cashPrice
      maxInstallmentsWithoutInterest
      installments { count value totalValue hasInterest }
    }
  }
`
