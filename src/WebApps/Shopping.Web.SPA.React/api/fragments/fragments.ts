// originalPrice/price/cashPrice/maxInstallmentsWithoutInterest are denormalized onto CatalogView's
// search document via CDC (ADR-0026/0028/0030) and returned directly on Product — no separate
// Pricing query/merge needed. The detailed per-installment plan is NOT here: it's fetched on demand
// via installmentPlanFor when the product detail page needs it, so it never gets denormalized onto
// a table nobody reads off the catalog/card views.
export const PRODUCT_FIELDS = `
  fragment ProductFields on Product {
    id name description imageUrl stock categoryIds averageRating ratingCount
    originalPrice price cashPrice maxInstallmentsWithoutInterest maxInstallmentValue
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
