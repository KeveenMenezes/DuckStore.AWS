export interface User {
  id: string
  name: string
  email: string
}

export type OrderStatus = "processing" | "shipped" | "delivered"

export interface OrderItem {
  name: string
  quantity: number
  price: number
}

export type PaymentMethodLabel = "Debit Card" | "Credit Card" | "Cash"

export interface OrderShippingAddress {
  firstName: string
  lastName: string
  emailAddress: string
  addressLine: string
  country: string
  state: string
  zipCode: string
}

export interface OrderPayment {
  method: PaymentMethodLabel
  installments: number
}

export interface Order {
  id: string
  date: string
  items: OrderItem[]
  total: number
  status: OrderStatus
  shippingAddress?: OrderShippingAddress
  payment?: OrderPayment
}

/** Input shape for creating an order; server-managed fields are added by the service. */
export type NewOrderInput = Omit<Order, "id" | "date" | "status">
