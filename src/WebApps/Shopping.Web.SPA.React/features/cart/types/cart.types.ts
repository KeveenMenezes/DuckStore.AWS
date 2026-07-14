export interface CartProduct {
  id: string
  name: string
  price: number
  // Snapshot of the product's main imageId at add-to-cart time (ADR-0034) — a key, never
  // a URL. Null for items persisted before the image pipeline (renders a placeholder).
  imageId: string | null
  stock?: number
}

export interface CartItem {
  product: CartProduct
  quantity: number
}
