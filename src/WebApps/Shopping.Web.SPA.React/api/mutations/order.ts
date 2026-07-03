// $ownerId is declared but never sent by the browser — the /api/graphql BFF injects it from the
// httpOnly identity cookies (see lib/basket-bff.ts). checkoutBasket/mergeBasket are Cognito-only
// and derive the owner from the token in the AppSync resolver.
export const STORE_BASKET = `
  mutation StoreBasket($ownerId: String!, $input: ShoppingCartInput!) {
    storeBasket(ownerId: $ownerId, input: $input) { ownerId }
  }
`

export const CHECKOUT_BASKET = `
  mutation CheckoutBasket($input: CheckoutInput!) {
    checkoutBasket(input: $input) { isSuccess }
  }
`

export const DELETE_BASKET = `
  mutation DeleteBasket($ownerId: String!) {
    deleteBasket(ownerId: $ownerId) { isSuccess }
  }
`

export const MERGE_BASKET = `
  mutation MergeBasket($guestId: String!) {
    mergeBasket(guestId: $guestId) { ownerId }
  }
`
