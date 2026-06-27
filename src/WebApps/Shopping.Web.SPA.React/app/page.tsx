import type { Metadata } from "next"
import { HeroSection } from "@/shared/layout/hero-section"
import { ProductCatalog } from "@/features/products/components/product-catalog"
import { getProducts, getRawCategories, buildCategories } from "@/features/products/services/products.service"

// ISR: product catalog — invalidated via webhook when DynamoDB changes.
export const revalidate = false

export const metadata: Metadata = {
  title: "CodeDuck Store - Duck Catalog",
  description: "Custom rubber ducks for devs. Pick the perfect duck to debug your code.",
}

export default async function HomePage() {
  const fetchInit: RequestInit = { cache: 'force-cache', next: { tags: ['products'] } }
  const [products, rawCategories] = await Promise.all([getProducts(100, fetchInit), getRawCategories(fetchInit)])
  const categories = buildCategories(rawCategories, products)

  return (
    <>
      <HeroSection />
      <ProductCatalog initialProducts={products} initialCategories={categories} />
    </>
  )
}
