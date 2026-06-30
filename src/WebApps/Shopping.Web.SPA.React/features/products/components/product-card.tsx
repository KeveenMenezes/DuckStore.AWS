"use client"

import Image from "next/image"
import Link from "next/link"
import { ShoppingCart } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { useCart } from "@/features/cart/hooks/use-cart"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import type { Product } from "@/features/products/types/product.types"
import { formatBRL } from "@/shared/lib/format"
import { ROUTES } from "@/shared/constants/routes"
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
      <Link href={ROUTES.product(product.id)} className="block">
        <div className="relative aspect-square overflow-hidden">
          <Image
            src={product.imageUrl}
            alt={product.name}
            fill
            className="object-cover transition-transform duration-300 group-hover:scale-105"
          />
        </div>
      </Link>
      <CardContent className="flex flex-col gap-3 p-4">
        <Link href={ROUTES.product(product.id)} className="block">
          <h3 className="font-semibold text-foreground">{product.name}</h3>
          <p className="mt-1 text-sm text-muted-foreground line-clamp-2">{product.description}</p>
          {product.ratingCount > 0 && (
            <div className="mt-1.5">
              <StarRatingDisplay rating={product.averageRating} count={product.ratingCount} size="sm" />
            </div>
          )}
        </Link>
        <div className="flex items-center justify-between">
          <span className="text-2xl font-bold text-primary">
            {formatBRL(product.price)}
          </span>
          <Button
            size="sm"
            className="gap-1.5"
            onClick={handleAddToCart}
            disabled={feedback === "success"}
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
