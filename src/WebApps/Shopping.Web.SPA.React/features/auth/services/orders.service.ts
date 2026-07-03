import { gql } from "@/api"
import { GET_ORDERS_BY_CUSTOMER } from "@/api/queries/orders"
import type { Order, OrderStatus } from "@/features/auth/types/auth.types"
import type { GqlOrderPage } from "@/graphql/types"

function mapBackendStatus(s: string): OrderStatus {
  if (s === "2" || /ship/i.test(s)) return "shipped"
  if (s === "3" || /deliv/i.test(s)) return "delivered"
  return "processing"
}

export async function getOrdersForCustomer(customerId: string): Promise<Order[]> {
  const data = await gql<{ ordersByCustomer: GqlOrderPage }>(GET_ORDERS_BY_CUSTOMER, { customerId })
  return (data.ordersByCustomer.items ?? []).map((o) => ({
    id: o.id,
    date: o.createdAt ?? new Date().toISOString(),
    status: mapBackendStatus(o.status),
    total: o.orderItems.reduce((sum, i) => sum + i.price * i.quantity, 0),
    items: o.orderItems.map((i) => ({
      name: `Product ${i.productId.slice(0, 8)}`,
      quantity: i.quantity,
      price: i.price,
    })),
  }))
}
