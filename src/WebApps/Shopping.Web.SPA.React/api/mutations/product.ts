export const CREATE_PRODUCT = `
  mutation CreateProduct($input: CreateProductInput!) {
    createProduct(input: $input) { id }
  }
`

export const UPDATE_PRODUCT = `
  mutation UpdateProduct($input: UpdateProductInput!) {
    updateProduct(input: $input) { id }
  }
`

export const DELETE_PRODUCT = `
  mutation DeleteProduct($id: ID!) {
    deleteProduct(id: $id) { isSuccess }
  }
`
