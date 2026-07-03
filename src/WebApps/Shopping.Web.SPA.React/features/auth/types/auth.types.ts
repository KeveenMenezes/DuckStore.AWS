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

export interface Order {
  id: string
  date: string
  items: OrderItem[]
  total: number
  status: OrderStatus
}

/** Input shape for creating an order; server-managed fields are added by the service. */
export type NewOrderInput = Omit<Order, "id" | "date" | "status">
