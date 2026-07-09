/** TypeScript interfaces aligned with graphql/schema.graphql. Keep in sync when the schema changes. */

export interface GqlInstallmentOption {
  count: number
  value: number
  totalValue: number
  hasInterest: boolean
}

export interface GqlProduct {
  id: string
  name: string
  description: string
  imageUrl: string
  stock: number
  categoryIds: string[]
  averageRating: number
  ratingCount: number
  // Denormalized onto CatalogView's search document via CDC (ADR-0026/0027/0028/0030) — scalars
  // only; the detailed per-installment plan is fetched separately via installmentPlanFor.
  originalPrice: number
  price: number
  cashPrice: number
  maxInstallmentsWithoutInterest: number
  maxInstallmentValue: number
}

// Nominal price and current discount live in Pricing, not Catalog (ADR-0026).
export interface GqlPrice {
  productId: string
  nominalPrice: number
  cost: number
  updatedAt: string
}

export interface GqlDiscount {
  productId: string
  campaignId: string
  discountType: string
  value: number
  startsAt: string
  endsAt: string
}

export interface GqlInstallmentPlan {
  productId: string
  originalPrice: number
  price: number
  cashPrice: number
  maxInstallmentsWithoutInterest: number
  installments: GqlInstallmentOption[]
}

// Cart-level equivalent of GqlInstallmentPlan — totals summed across every item first, then run
// through the same cost-floor calculation as a single checkout transaction.
export interface GqlBasketInstallmentPlan {
  totalOriginalPrice: number
  price: number
  cashPrice: number
  maxInstallmentsWithoutInterest: number
  installments: GqlInstallmentOption[]
}

export interface BasketInstallmentItemInput {
  productId: string
  quantity: number
}

// One payment-gateway provider's operating costs (ADR-0028).
export interface GqlGatewayCost {
  provider: string
  flatFeePerTransaction: number
  avistaRatePercent: number
  installmentRates: Record<string, number>
}

export interface GqlCampaign {
  id: string
  name: string
  discountType: string
  value: number
  startsAt: string
  endsAt: string
  productIds: string[]
}

export interface GqlReview {
  id: string
  productId: string
  userName: string
  rating: number
  comment: string
  createdAt: string
  updatedAt: string
}

export interface GqlReviewPage {
  items: GqlReview[]
  nextToken: string | null
}

export interface GqlCreateReviewResult {
  id: string
}

export interface GqlProductPage {
  items: GqlProduct[]
  nextToken: string | null
}

export interface GqlCategory {
  id: string
  name: string
  parentId: string | null
  path: string[]
}

export interface GqlCategoryPage {
  items: GqlCategory[]
  nextToken: string | null
}

export interface GqlCartItem {
  quantity: number
  color: string | null
  price: number
  productId: string
  productName: string
  imageUrl: string | null
}

export interface GqlShoppingCart {
  ownerId: string
  items: GqlCartItem[]
  totalPrice: number
}

export interface GqlStoreBasketResult {
  ownerId: string
}

export interface GqlCheckoutResult {
  isSuccess: boolean
}

export interface GqlDeleteBasketResult {
  isSuccess: boolean
}

export interface GqlMergeBasketResult {
  ownerId: string
}

export interface GqlUserProfile {
  userId: string
  email: string
  name: string
  phone: string | null
  addressLine: string | null
  city: string | null
  state: string | null
  zipCode: string | null
  country: string | null
}

export interface GqlCreateProductResult {
  id: string
}

export interface GqlUpdateProductResult {
  id: string
}

export interface GqlDeleteProductResult {
  isSuccess: boolean
}

export interface GqlShippingAddress {
  firstName: string
  lastName: string
  emailAddress: string
  addressLine: string
  country: string
  state: string
  zipCode: string
}

export interface GqlOrderPayment {
  // Null for Cash — no card is collected for that method.
  cardName: string | null
  cardNumber: string | null
  expiration: string | null
  cvv: string | null
  paymentMethod: number
  installments: number
}

export interface GqlOrderItem {
  productId: string
  quantity: number
  price: number
}

export interface GqlOrder {
  id: string
  customerId: string
  orderName: string
  status: string
  createdAt: string | null
  shippingAddress: GqlShippingAddress
  payment: GqlOrderPayment
  orderItems: GqlOrderItem[]
}

export interface GqlOrderPage {
  items: GqlOrder[]
  nextToken: string | null
}

export interface GqlDeleteOrderResult {
  isSuccess: boolean
}

// Input types

export interface CartItemInput {
  quantity: number
  color?: string | null
  price: number
  productId: string
  productName: string
  imageUrl?: string | null
}

export interface ShoppingCartInput {
  items: CartItemInput[]
}

export interface CheckoutInput {
  totalPrice: number
  firstName: string
  lastName: string
  emailAddress: string
  addressLine: string
  country: string
  state: string
  zipCode: string
  // Optional — omitted (or null) when paymentMethod is Cash.
  cardName?: string | null
  cardNumber?: string | null
  expiration?: string | null
  cvv?: string | null
  paymentMethod: number
  installments: number
}

export interface CreateProductInput {
  name: string
  description: string
  imageUrl: string
  stock: number
  categoryIds: string[]
}

export interface UpdateProductInput {
  id: string
  name: string
  description: string
  imageUrl: string
  stock: number
  categoryIds: string[]
}

export interface CreateReviewInput {
  productId: string
  userName: string
  rating: number
  comment: string
}

export interface UpdateProfileInput {
  name: string
  phone?: string | null
  addressLine?: string | null
  city?: string | null
  state?: string | null
  zipCode?: string | null
  country?: string | null
}
