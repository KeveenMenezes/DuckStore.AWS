import { gql } from '@/api'
import { STORE_BASKET } from '@/api/mutations/order'
import type { CartItem } from '@/features/cart/types/cart.types'
import type { GqlStoreBasketResult } from '@/graphql/types'

// The ownerId is resolved and injected server-side by the /api/graphql BFF from the httpOnly
// identity cookies (USER#<sub> or GUEST#<guestId>) — the browser never holds or sends it.
export async function syncCartToBasket(items: CartItem[]): Promise<void> {
  await gql<{ storeBasket: GqlStoreBasketResult }>(STORE_BASKET, {
    input: {
      items: items.map((i) => ({
        quantity: i.quantity,
        price: i.product.price,
        productId: i.product.id,
        productName: i.product.name,
        imageId: i.product.imageId,
        color: null,
      })),
    },
  })
}
