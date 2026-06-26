import { storage } from "@/shared/lib/storage"
import { STORAGE_KEYS } from "@/shared/constants/storage-keys"
import { createOrderId } from "@/shared/lib/id"
import type { NewOrderInput, Order } from "@/features/auth/types/auth.types"

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
