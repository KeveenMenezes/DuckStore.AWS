/** Product as returned by the GraphQL API (sourced from DynamoDB Products table). */
export interface Product {
  id: string
  name: string
  description: string
  imageUrl: string
  price: number
  stock: number
  categoryIds: string[]
  averageRating: number
  ratingCount: number
}

/** A selectable catalog category with a client-derived product count. */
export interface ProductCategory {
  id: string
  name: string
  count: number
}
