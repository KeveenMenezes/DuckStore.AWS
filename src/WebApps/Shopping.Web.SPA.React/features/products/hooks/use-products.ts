"use client"

import { useMemo, useState } from "react"
import {
  ALL_CATEGORY_ID,
  getCategories,
  getProducts,
  getProductsByCategory,
} from "@/features/products/services/products.service"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

interface UseProductsInitial {
  products?: Product[]
  categories?: ProductCategory[]
}

/**
 * Owns catalog category-filter state and derives the visible product list.
 * Optionally seeds from server-fetched data (ISR) so the hydrated client
 * filters exactly the catalog the Server Component rendered.
 */
export function useProducts(initial?: UseProductsInitial) {
  const [activeCategory, setActiveCategory] = useState<string>(ALL_CATEGORY_ID)

  const allProducts = useMemo(() => initial?.products ?? getProducts(), [initial?.products])
  const categories = useMemo(() => initial?.categories ?? getCategories(), [initial?.categories])
  const filteredProducts = useMemo(
    () => getProductsByCategory(activeCategory, allProducts),
    [activeCategory, allProducts],
  )

  return { categories, activeCategory, setActiveCategory, filteredProducts }
}
