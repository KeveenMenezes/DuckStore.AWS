import { gql } from '@/api'
import { GET_BASKET } from '@/api/queries/order'
import { STORE_BASKET } from '@/api/mutations/order'
import type { CartItem } from '@/features/cart/types/cart.types'
import type { GqlShoppingCart, GqlStoreBasketResult } from '@/graphql/types'

/** Anti-corruption boundary: translates the wire-shaped `GqlShoppingCart` into `CartItem[]`. */
function toCartItems(gql: GqlShoppingCart | null): CartItem[] {
  return (gql?.items ?? []).map((item) => ({
    product: {
      id: item.productId,
      name: item.productName,
      price: item.price,
      imageId: item.imageId ?? null,
    },
    quantity: item.quantity,
  }))
}

// No ownerId passed — the /api/graphql BFF injects it from the httpOnly identity cookies.
export async function getBasket(): Promise<CartItem[]> {
  const data = await gql<{ basket: GqlShoppingCart | null }>(GET_BASKET)
  return toCartItems(data.basket)
}

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
