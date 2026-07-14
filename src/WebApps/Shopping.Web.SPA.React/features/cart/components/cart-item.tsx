"use client"

import { Minus, Plus, Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { formatBRL } from "@/shared/lib/format"
import type { CartItem as CartItemType } from "@/features/cart/types/cart.types"
import { ProductPicture } from "@/features/products/components/product-picture"

interface CartItemProps {
  item: CartItemType
  onUpdateQuantity: (productId: string, quantity: number) => void
  onRemove: (productId: string) => void
}

export function CartItem({ item, onUpdateQuantity, onRemove }: CartItemProps) {
  return (
    <div className="flex gap-4 rounded-lg border border-border bg-card p-3">
      <div className="relative h-20 w-20 flex-shrink-0 overflow-hidden rounded-md bg-secondary">
        {/* Thumb variant served straight from the image CDN via <picture> (ADR-0034). */}
        <ProductPicture
          imageId={item.product.imageId}
          alt={item.product.name}
          sizes="80px"
          fallbackWidth={160}
          className="absolute inset-0 h-full w-full object-cover"
        />
      </div>
      <div className="flex flex-1 flex-col justify-between">
        <div>
          <h3 className="text-sm font-medium text-foreground">{item.product.name}</h3>
          <p className="text-sm font-semibold text-primary">
            {formatBRL(item.product.price)}
          </p>
        </div>
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1">
            <Button
              variant="outline"
              size="icon"
              className="h-7 w-7"
              onClick={() => onUpdateQuantity(item.product.id, item.quantity - 1)}
              aria-label="Decrease quantity"
            >
              <Minus className="h-3 w-3" />
            </Button>
            <span className="w-8 text-center text-sm font-medium text-foreground">{item.quantity}</span>
            <Button
              variant="outline"
              size="icon"
              className="h-7 w-7"
              onClick={() => onUpdateQuantity(item.product.id, item.quantity + 1)}
              disabled={item.quantity >= (item.product.stock ?? Infinity)}
              aria-label="Increase quantity"
            >
              <Plus className="h-3 w-3" />
            </Button>
          </div>
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-destructive hover:text-destructive"
            onClick={() => onRemove(item.product.id)}
            aria-label="Remove item"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  )
}
