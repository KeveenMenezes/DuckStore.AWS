"use client"

import { useState } from "react"
import { ShoppingCart } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useCart } from "@/features/cart/hooks/use-cart"
import type { Product } from "@/features/products/types/product.types"

export function AddToCartButton({ product }: { product: Product }) {
  const { addItem, setIsOpen } = useCart()
  const [feedback, setFeedback] = useState<"success" | "error" | null>(null)

  const handleAddToCart = () => {
    const success = addItem(product)
    if (success) {
      setFeedback("success")
      setIsOpen(true)
    } else {
      setFeedback("error")
    }
    setTimeout(() => setFeedback(null), 2000)
  }

  const inStock = product.stock > 0

  return (
    <Button
      size="lg"
      className="w-full gap-2 text-base"
      onClick={handleAddToCart}
      disabled={!inStock || feedback === "success"}
    >
      <ShoppingCart className="h-5 w-5" />
      {feedback === "success"
        ? "Added to cart!"
        : feedback === "error"
          ? "Out of stock!"
          : inStock
            ? "Add to Cart"
            : "Out of Stock"}
    </Button>
  )
}
