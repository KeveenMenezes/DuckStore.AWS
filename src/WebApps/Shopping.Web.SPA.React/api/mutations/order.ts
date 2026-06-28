export const STORE_BASKET = `
  mutation StoreBasket($input: ShoppingCartInput!) {
    storeBasket(input: $input) { userName }
  }
`

export const CHECKOUT_BASKET = `
  mutation CheckoutBasket($input: CheckoutInput!) {
    checkoutBasket(input: $input) { isSuccess }
  }
`

export const DELETE_BASKET = `
  mutation DeleteBasket($userName: String!) {
    deleteBasket(userName: $userName) { isSuccess }
  }
`
