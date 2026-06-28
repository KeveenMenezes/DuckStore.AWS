import { gql } from '@/api'
import { STORE_BASKET } from '@/api/mutations/order'
import { storage } from '@/shared/lib/storage'
import { STORAGE_KEYS } from '@/shared/constants/storage-keys'
import type { CartItem } from '@/features/cart/types/cart.types'
import type { GqlStoreBasketResult } from '@/graphql/types'

export function getGuestUserName(): string {
  const existing = storage.getRaw(STORAGE_KEYS.guestUsername)
  if (existing) return existing
  const generated = `guest-${crypto.randomUUID()}`
  storage.setRaw(STORAGE_KEYS.guestUsername, generated)
  return generated
}

export function getGuestCustomerId(): string {
  const existing = storage.getRaw(STORAGE_KEYS.guestCustomerId)
  if (existing) return existing
  const generated = crypto.randomUUID()
  storage.setRaw(STORAGE_KEYS.guestCustomerId, generated)
  return generated
}

export async function syncCartToBasket(userName: string, items: CartItem[]): Promise<void> {
  await gql<{ storeBasket: GqlStoreBasketResult }>(STORE_BASKET, {
    input: {
      userName,
      items: items.map((i) => ({
        quantity: i.quantity,
        price: i.product.price,
        productId: i.product.id,
        productName: i.product.name,
        color: null,
      })),
    },
  })
}
