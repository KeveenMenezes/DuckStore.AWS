import type { Metadata } from "next"
import { HeroSection } from "@/shared/layout/hero-section"
import { ProductCatalog } from "@/features/products/components/product-catalog"
import { getCategories, getProducts } from "@/features/products/services/products.service"

// 🟡 ISR: product catalog — static HTML revalidated periodically.
export const revalidate = 300

export const metadata: Metadata = {
  title: "CodeDuck Store - Duck Catalog",
  description: "Custom rubber ducks for devs. Pick the perfect duck to debug your code.",
}

export default function HomePage() {
  // Server Component fetches the catalog (ISR-cached); the client catalog hydrates
  // filtering and add-to-cart on top of this data.
  const products = getProducts()
  const categories = getCategories()

  return (
    <>
      <HeroSection />
      <ProductCatalog initialProducts={products} initialCategories={categories} />
    </>
  )
}
