/** TypeScript interfaces aligned with graphql/schema.graphql. Keep in sync when the schema changes. */

export interface GqlProduct {
  id: string
  name: string
  description: string
  imageUrl: string
  price: number
  stock: number
  categoryIds: string[]
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
  userName: string
  items: GqlCartItem[]
  totalPrice: number
}

export interface GqlCoupon {
  productName: string
  description: string
  amount: number
}

export interface GqlStoreBasketResult {
  userName: string
}

export interface GqlCheckoutResult {
  isSuccess: boolean
}

export interface GqlDeleteBasketResult {
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
  userName: string
  items: CartItemInput[]
}

export interface CheckoutInput {
  userName: string
  customerId: string
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
