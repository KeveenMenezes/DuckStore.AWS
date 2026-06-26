"use client"

import { useCartContext } from "@/features/cart/context/cart-context"

/** Public hook for accessing cart state and actions. */
export function useCart() {
  return useCartContext()
}
