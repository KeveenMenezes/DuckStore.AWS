import { PRODUCT_FIELDS } from '../fragments/fragments'

export const GET_PRODUCTS = `
  ${PRODUCT_FIELDS}
  query GetProducts($pageSize: Int, $nextToken: String) {
    products(pageSize: $pageSize, nextToken: $nextToken) {
      items { ...ProductFields }
      nextToken
    }
  }
`

export const GET_PRODUCT = `
  ${PRODUCT_FIELDS}
  query GetProduct($id: ID!) {
    product(id: $id) { ...ProductFields }
  }
`
