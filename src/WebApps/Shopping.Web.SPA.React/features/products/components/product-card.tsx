"use client"

import Image from "next/image"
import { ShoppingCart, Package } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { useCart } from "@/features/cart/hooks/use-cart"
import type { Product } from "@/features/products/types/product.types"
import { formatBRL } from "@/shared/lib/format"
import { useState } from "react"

export function ProductCard({ product }: { product: Product }) {
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

  return (
    <Card className="group overflow-hidden border-border bg-card transition-all hover:border-primary/30 hover:shadow-lg hover:shadow-primary/5">
      <div className="relative aspect-square overflow-hidden">
        <Image
          src={product.imageUrl}
          alt={product.name}
          fill
          className="object-cover transition-transform duration-300 group-hover:scale-105"
        />
        <div className="absolute right-2 top-2 flex flex-col gap-1">
          {product.stock <= 5 && product.stock > 0 && (
            <Badge variant="destructive" className="text-xs">
              Last units!
            </Badge>
          )}
          {product.stock === 0 && (
            <Badge variant="secondary" className="text-xs">
              Sold out
            </Badge>
          )}
        </div>
      </div>
      <CardContent className="flex flex-col gap-3 p-4">
        <div>
          <h3 className="font-semibold text-foreground">{product.name}</h3>
          <p className="mt-1 text-sm text-muted-foreground line-clamp-2">{product.description}</p>
        </div>
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <Package className="h-3.5 w-3.5" />
          <span>{product.stock > 0 ? `${product.stock} in stock` : "Out of stock"}</span>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-2xl font-bold text-primary">
            {formatBRL(product.price)}
          </span>
          <Button
            size="sm"
            className="gap-1.5"
            onClick={handleAddToCart}
            disabled={product.stock === 0 || feedback === "success"}
          >
            <ShoppingCart className="h-4 w-4" />
            {feedback === "success"
              ? "Added!"
              : feedback === "error"
                ? "Out of stock!"
                : "Buy"}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
