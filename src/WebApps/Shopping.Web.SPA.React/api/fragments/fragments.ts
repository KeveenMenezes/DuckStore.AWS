export const PRODUCT_FIELDS = `
  fragment ProductFields on Product {
    id name description imageUrl price stock categoryIds averageRating ratingCount
  }
`

export const CART_ITEM_FIELDS = `
  fragment CartItemFields on CartItem {
    quantity color price productId productName imageUrl
  }
`

export const SHOPPING_CART_FIELDS = `
  fragment ShoppingCartFields on ShoppingCart {
    ownerId totalPrice
    items { ...CartItemFields }
  }
`
