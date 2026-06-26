export interface Product {
  id: string
  name: string
  description: string
  longDescription: string
  price: number
  image: string
  category: string
  tags: string[]
  stock: number
  rating: number
  reviews: number
}

/** A selectable catalog category along with the number of products in it. */
export interface ProductCategory {
  id: string
  name: string
  count: number
}
