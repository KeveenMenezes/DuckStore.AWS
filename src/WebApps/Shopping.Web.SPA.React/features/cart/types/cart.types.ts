export interface CartProduct {
  id: string
  name: string
  price: number
  imageUrl: string
  stock?: number
}

export interface CartItem {
  product: CartProduct
  quantity: number
}
