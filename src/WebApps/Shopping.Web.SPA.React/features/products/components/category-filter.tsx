"use client"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import type { ProductCategory } from "@/features/products/types/product.types"

interface CategoryFilterProps {
  categories: ProductCategory[]
  activeCategory: string
  onSelect: (categoryId: string) => void
}

export function CategoryFilter({ categories, activeCategory, onSelect }: CategoryFilterProps) {
  return (
    <div className="mb-8 flex flex-wrap gap-2">
      {categories.map((cat) => {
        const isActive = activeCategory === cat.id
        return (
          <Button
            key={cat.id}
            variant={isActive ? "default" : "outline"}
            size="sm"
            onClick={() => onSelect(cat.id)}
            className={cn("gap-1.5", isActive && "shadow-md shadow-primary/20")}
          >
            {cat.name}
            <span
              className={cn(
                "rounded-full px-1.5 text-xs",
                isActive
                  ? "bg-primary-foreground/20 text-primary-foreground"
                  : "bg-secondary text-muted-foreground",
              )}
            >
              {cat.count}
            </span>
          </Button>
        )
      })}
    </div>
  )
}
