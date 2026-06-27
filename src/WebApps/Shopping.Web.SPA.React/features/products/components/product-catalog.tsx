"use client"

import { useProducts } from "@/features/products/hooks/use-products"
import { ProductCard } from "@/features/products/components/product-card"
import { CategoryFilter } from "@/features/products/components/category-filter"
import type { Product, ProductCategory } from "@/features/products/types/product.types"

interface ProductCatalogProps {
  initialProducts: Product[]
  initialCategories: ProductCategory[]
}

export function ProductCatalog({ initialProducts, initialCategories }: ProductCatalogProps) {
  const { categories, activeCategory, setActiveCategory, filteredProducts } = useProducts({
    products: initialProducts,
    categories: initialCategories,
  })

  return (
    <section id="catalog" className="mx-auto max-w-7xl px-4 py-16 lg:px-8">
      <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-3xl font-bold text-foreground">Duck Catalog</h2>
          <p className="mt-2 text-muted-foreground">
            Pick the perfect duck to debug your code
          </p>
        </div>
      </div>

      <CategoryFilter
        categories={categories}
        activeCategory={activeCategory}
        onSelect={setActiveCategory}
      />

      <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {filteredProducts.map((product) => (
          <ProductCard key={product.id} product={product} />
        ))}
      </div>

      {filteredProducts.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <p className="text-lg text-muted-foreground">No ducks found in this category.</p>
        </div>
      )}
    </section>
  )
}
