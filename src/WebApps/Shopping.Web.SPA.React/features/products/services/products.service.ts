import { gqlPublic } from "@/api"
import { GET_PRODUCTS, GET_PRODUCT } from "@/api/queries/product"
import { GET_CATEGORIES } from "@/api/queries/category"
import { GET_INSTALLMENT_PLAN } from "@/api/queries/pricing"
import type { GqlProductPage, GqlCategoryPage, GqlProduct, GqlInstallmentPlan } from "@/graphql/types"
import type {
  Product,
  ProductCategory,
  InstallmentPlan,
} from "@/features/products/types/product.types"

export const ALL_CATEGORY_ID = "all"

// Public catalog reads use the cookie-free `gqlPublic` client so the Server
// Components that call them (home, product detail) stay statically renderable.

/**
 * Anti-corruption boundary: translates the wire-shaped `GqlProduct` (mirrors graphql/schema.graphql)
 * into the domain `Product` type the rest of the app consumes. Field-by-field on purpose — even
 * though the two shapes are identical today, this is what actually makes graphql/types.ts safe to
 * change independently of product.types.ts: adding/removing/renaming a field on either side now
 * surfaces as a compiler error here instead of silently drifting (see product.types.ts).
 */
function toProduct(gql: GqlProduct): Product {
  return {
    id: gql.id,
    name: gql.name,
    description: gql.description,
    images: gql.images,
    stock: gql.stock,
    categoryIds: gql.categoryIds,
    averageRating: gql.averageRating,
    ratingCount: gql.ratingCount,
    ratingDistribution: parseRatingDistribution(gql.ratingDistribution),
    originalPrice: gql.originalPrice,
    price: gql.price,
    cashPrice: gql.cashPrice,
    maxInstallmentsWithoutInterest: gql.maxInstallmentsWithoutInterest,
    maxInstallmentValue: gql.maxInstallmentValue,
  }
}

// AWSJSON is opaque per the GraphQL spec: real AppSync serializes it as a JSON-encoded STRING
// (must be JSON.parse'd), while the local dev backend's scalar (app/api/graphql/local.ts) passes
// the native object through unparsed — same field, different shape depending on environment.
function parseRatingDistribution(value: unknown): Record<string, number> {
  if (typeof value === "string") {
    try {
      return JSON.parse(value) as Record<string, number>
    } catch {
      return {}
    }
  }
  return (value as Record<string, number>) ?? {}
}

/** Fetch the full product catalog from GraphQL (used by Server Components). */
export async function getProducts(pageSize = 100, init?: RequestInit): Promise<Product[]> {
  const data = await gqlPublic<{ products: GqlProductPage }>(GET_PRODUCTS, { pageSize }, init)
  return data.products.items.map(toProduct)
}

/** Fetch a single product by id (used by Server Components). */
export async function getProduct(id: string, init?: RequestInit): Promise<Product> {
  const data = await gqlPublic<{ product: GqlProduct }>(GET_PRODUCT, { id }, init)
  return toProduct(data.product)
}

/** Anti-corruption boundary for `GqlInstallmentPlan` — see `toProduct` above. */
function toInstallmentPlan(gql: GqlInstallmentPlan): InstallmentPlan {
  return {
    productId: gql.productId,
    originalPrice: gql.originalPrice,
    price: gql.price,
    cashPrice: gql.cashPrice,
    maxInstallmentsWithoutInterest: gql.maxInstallmentsWithoutInterest,
    installments: gql.installments,
  }
}

/**
 * Fetch the full, synchronously-computed installment breakdown for one product (used by the
 * product detail page to render the payment-methods modal). Not part of the Product document —
 * recomputed live by Pricing on every call.
 */
export async function getInstallmentPlan(
  productId: string,
  init?: RequestInit,
): Promise<InstallmentPlan | null> {
  const data = await gqlPublic<{ installmentPlanFor: GqlInstallmentPlan | null }>(
    GET_INSTALLMENT_PLAN,
    { productId },
    init,
  )
  return data.installmentPlanFor ? toInstallmentPlan(data.installmentPlanFor) : null
}

/** Fetch all categories from GraphQL. */
export async function getRawCategories(init?: RequestInit): Promise<Array<{ id: string; name: string }>> {
  const data = await gqlPublic<{ categories: GqlCategoryPage }>(GET_CATEGORIES, { pageSize: 50 }, init)
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
