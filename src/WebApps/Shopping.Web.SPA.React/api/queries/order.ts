import { CART_ITEM_FIELDS, SHOPPING_CART_FIELDS } from '../fragments/fragments'

export const GET_BASKET = `
  ${CART_ITEM_FIELDS}
  ${SHOPPING_CART_FIELDS}
  query GetBasket($userName: String!) {
    basket(userName: $userName) { ...ShoppingCartFields }
  }
`

export const GET_COUPON_FOR = `
  query GetCouponFor($productName: String!) {
    couponFor(productName: $productName) {
      productName description amount
    }
  }
`
