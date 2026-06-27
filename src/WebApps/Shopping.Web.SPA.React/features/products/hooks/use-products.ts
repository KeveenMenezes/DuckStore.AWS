"use client"

import { useMemo, useState } from "react"
import {
  ALL_CATEGORY_ID,
  getProductsByCategory,
} from "@/features/products/services/products.service"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

interface UseProductsInitial {
  products: Product[]
  categories: ProductCategory[]
}

/**
 * Owns catalog category-filter state and derives the visible product list.
 * Data is always seeded from the Server Component (ISR); no client-side fallback fetch.
 */
export function useProducts(initial: UseProductsInitial) {
  const [activeCategory, setActiveCategory] = useState<string>(ALL_CATEGORY_ID)

  const filteredProducts = useMemo(
    () => getProductsByCategory(activeCategory, initial.products),
    [activeCategory, initial.products],
  )

  return { categories: initial.categories, activeCategory, setActiveCategory, filteredProducts }
}
