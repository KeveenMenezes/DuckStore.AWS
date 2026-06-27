import { gql } from "@/shared/lib/graphql-client"
import type { GqlProductPage, GqlCategoryPage } from "@/graphql/types"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

export const ALL_CATEGORY_ID = "all"

const PRODUCTS_QUERY = `
  query GetProducts($pageSize: Int, $nextToken: String) {
    products(pageSize: $pageSize, nextToken: $nextToken) {
      items {
        id name description imageUrl price stock categoryIds
      }
      nextToken
    }
  }
`

const CATEGORIES_QUERY = `
  query GetCategories($pageSize: Int) {
    categories(pageSize: $pageSize) {
      items { id name }
    }
  }
`

/** Fetch the full product catalog from GraphQL (used by Server Components). */
export async function getProducts(pageSize = 100, init?: RequestInit): Promise<Product[]> {
  const data = await gql<{ products: GqlProductPage }>(PRODUCTS_QUERY, { pageSize }, init)
  return data.products.items
}

/** Fetch all categories from GraphQL. */
export async function getRawCategories(init?: RequestInit): Promise<Array<{ id: string; name: string }>> {
  const data = await gql<{ categories: GqlCategoryPage }>(CATEGORIES_QUERY, { pageSize: 50 }, init)
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
