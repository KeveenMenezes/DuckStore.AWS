// Detailed per-installment breakdown, computed synchronously by Pricing on every call — never
// denormalized onto Product/OpenSearch (see fragments.ts). Fetched by the product detail page only.
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
