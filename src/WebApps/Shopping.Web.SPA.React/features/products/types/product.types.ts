export interface InstallmentOption {
  count: number
  value: number
  totalValue: number
  hasInterest: boolean
}

/**
 * Full installment breakdown for one product, computed synchronously by Pricing's
 * GetInstallmentPlan query (installmentPlanFor) — fetched on demand by the product detail page,
 * never denormalized onto Product/OpenSearch.
 */
export interface InstallmentPlan {
  productId: string
  originalPrice: number
  price: number
  cashPrice: number
  maxInstallmentsWithoutInterest: number
  installments: InstallmentOption[]
}

/** Product as returned by the GraphQL API (sourced from CatalogView's OpenSearch index). */
export interface Product {
  id: string
  name: string
  description: string
  imageUrl: string
  // Sticker/"De" price — never discounted, denormalized onto Product.
  originalPrice: number
  // Payment highlights — cost-derived, already reflects an active campaign discount if any.
  price: number
  cashPrice: number
  maxInstallmentsWithoutInterest: number
  // Exact per-installment $ value at maxInstallmentsWithoutInterest — never divide price
  // client-side, this is the authoritative figure from Pricing.
  maxInstallmentValue: number
  stock: number
  categoryIds: string[]
  averageRating: number
  ratingCount: number
}

/** A selectable catalog category with a client-derived product count. */
export interface ProductCategory {
  id: string
  name: string
  count: number
}
