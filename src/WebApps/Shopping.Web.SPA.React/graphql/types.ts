/** TypeScript interfaces aligned with graphql/schema.graphql. Keep in sync when the schema changes. */

export interface GqlProduct {
  id: string
  name: string
  description: string
  imageUrl: string
  price: number
  stock: number
  categoryIds: string[]
  averageRating: number
  ratingCount: number
}

export interface GqlReview {
  id: string
  productId: string
  userName: string
  rating: number
  comment: string
  createdAt: string
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
}

export interface GqlShoppingCart {
  ownerId: string
  items: GqlCartItem[]
  totalPrice: number
}

export interface GqlCoupon {
  productName: string
  description: string
  amount: number
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
  cardName: string
  cardNumber: string
  expiration: string
  cvv: string
  paymentMethod: number
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
  cardName: string
  cardNumber: string
  expiration: string
  cvv: string
  paymentMethod: number
}

export interface CreateProductInput {
  name: string
  description: string
  imageUrl: string
  price: number
  stock: number
  categoryIds: string[]
}

export interface UpdateProductInput {
  id: string
  name: string
  description: string
  imageUrl: string
  price: number
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
