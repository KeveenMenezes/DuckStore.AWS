import type { Product } from "@/features/products/types/product.types"

export interface CartItem {
  product: Product
  quantity: number
}
