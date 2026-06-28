import { gql } from "@/api"
import { GET_PRODUCTS, GET_PRODUCT } from "@/api/queries/product"
import { GET_CATEGORIES } from "@/api/queries/category"
import type { GqlProductPage, GqlCategoryPage, GqlProduct } from "@/graphql/types"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

export const ALL_CATEGORY_ID = "all"

/** Fetch the full product catalog from GraphQL (used by Server Components). */
export async function getProducts(pageSize = 100, init?: RequestInit): Promise<Product[]> {
  const data = await gql<{ products: GqlProductPage }>(GET_PRODUCTS, { pageSize }, init)
  return data.products.items
}

/** Fetch a single product by id (used by Server Components). */
export async function getProduct(id: string, init?: RequestInit): Promise<Product> {
  const data = await gql<{ product: GqlProduct }>(GET_PRODUCT, { id }, init)
  return data.product
}

/** Fetch all categories from GraphQL. */
export async function getRawCategories(init?: RequestInit): Promise<Array<{ id: string; name: string }>> {
  const data = await gql<{ categories: GqlCategoryPage }>(GET_CATEGORIES, { pageSize: 50 }, init)
  return data.categories.items
}

/**
 * Filter products by category ID (client-side).
 * `source` must be passed explicitly — no longer reads static data.
 */
export function getProductsByCategory(categoryId: string, source: Product[]): Product[] {
  if (categoryId === ALL_CATEGORY_ID) return source
  return source.filter((p) => p.categoryIds.includes(categoryId))
}

/** Build the category filter list with product counts derived from the loaded catalog. */
export function buildCategories(
  rawCategories: Array<{ id: string; name: string }>,
  allProducts: Product[],
): ProductCategory[] {
  const all: ProductCategory = { id: ALL_CATEGORY_ID, name: "All", count: allProducts.length }
  const rest = rawCategories.map((cat) => ({
    id: cat.id,
    name: cat.name,
    count: allProducts.filter((p) => p.categoryIds.includes(cat.id)).length,
  }))
  return [all, ...rest]
}
