import { CART_ITEM_FIELDS, SHOPPING_CART_FIELDS } from '../fragments/fragments'

export const GET_BASKET = `
  ${CART_ITEM_FIELDS}
  ${SHOPPING_CART_FIELDS}
  query GetBasket($ownerId: String!) {
    basket(ownerId: $ownerId) { ...ShoppingCartFields }
  }
`

