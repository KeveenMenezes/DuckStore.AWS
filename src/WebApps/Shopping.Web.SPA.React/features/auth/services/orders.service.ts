import { storage } from "@/shared/lib/storage"
import { STORAGE_KEYS } from "@/shared/constants/storage-keys"
import { createOrderId } from "@/shared/lib/id"
import { gql } from "@/api"
import { GET_ORDERS_BY_CUSTOMER } from "@/api/queries/orders"
import type { NewOrderInput, Order, OrderStatus } from "@/features/auth/types/auth.types"
import type { GqlOrderPage } from "@/graphql/types"

export const ordersService = {
  getForUser(userId: string): Order[] {
    return storage.get<Order[]>(STORAGE_KEYS.orders(userId), [])
  },

  /** Builds a full order from raw input and persists it at the front of the user's history. */
  addForUser(userId: string, input: NewOrderInput): Order[] {
    const newOrder: Order = {
      ...input,
      id: createOrderId(),
      date: new Date().toISOString(),
      status: "processing",
    }
    const updated = [newOrder, ...this.getForUser(userId)]
    storage.set(STORAGE_KEYS.orders(userId), updated)
    return updated
  },
}

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
