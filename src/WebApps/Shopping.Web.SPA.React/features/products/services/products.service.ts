import { products } from "@/features/products/data/products.data"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

/** Sentinel category id representing "all products". */
export const ALL_CATEGORY_ID = "all"

/** Ordered category definitions (excluding the "all" entry). */
const CATEGORY_DEFINITIONS: ReadonlyArray<{ id: string; name: string }> = [
  { id: "classics", name: "Classics" },
  { id: "languages", name: "Languages" },
  { id: "frameworks", name: "Frameworks" },
  { id: "specials", name: "Specials" },
]

/** Return the full product catalog. */
export function getProducts(): Product[] {
  return products
}

/**
 * Return products for a category, or all products for the "all" sentinel.
 * `source` defaults to the full catalog but can be a server-fetched list so
 * the client filters the same data the Server Component rendered.
 */
export function getProductsByCategory(categoryId: string, source: Product[] = products): Product[] {
  if (categoryId === ALL_CATEGORY_ID) return source
  return source.filter((product) => product.category === categoryId)
}

/** Build the category filter list with up-to-date product counts. */
export function getCategories(): ProductCategory[] {
  return [
    { id: ALL_CATEGORY_ID, name: "All", count: products.length },
    ...CATEGORY_DEFINITIONS.map((category) => ({
      ...category,
      count: products.filter((product) => product.category === category.id).length,
    })),
  ]
}
