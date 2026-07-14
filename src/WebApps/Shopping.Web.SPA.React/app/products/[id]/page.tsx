import type { Metadata } from "next"
import { notFound } from "next/navigation"
import Link from "next/link"
import { ArrowLeft, Package } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Separator } from "@/components/ui/separator"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { ReviewsSection } from "@/features/reviews/components/reviews-section"
import { AddToCartButton } from "@/app/products/[id]/add-to-cart-button"
import { ProductPrice } from "@/features/products/components/product-price"
import { ProductGallery } from "@/features/products/components/product-gallery"
import {
  getProduct,
  getProducts,
  getInstallmentPlan,
} from "@/features/products/services/products.service"
import { getReviewsByProduct } from "@/features/reviews/services/reviews.service"
import { ROUTES } from "@/shared/constants/routes"

// ISR: product detail is the same for all users; invalidated per-product via
// the products:{id}/reviews:{id} tags (not the generic products/reviews tags
// the home page uses), so a change to one product doesn't revalidate every
// other product's page.
export const revalidate = false

// Prerender every product page at build (SSG) so `/products/[id]` is served
// from the ISR cache / CDN instead of rendered on the Lambda per request.
// Cookie-free (gqlPublic) — reading cookies here would force the route dynamic.
// dynamicParams stays true (default), so a product not in this list still
// renders on-demand and is then cached.
export async function generateStaticParams() {
  const products = await getProducts(100).catch(() => [])
  return products.map((product) => ({ id: product.id }))
}

interface ProductPageProps {
  params: Promise<{ id: string }>
}

export async function generateMetadata({ params }: ProductPageProps): Promise<Metadata> {
  const { id } = await params
  const product = await getProduct(id, { cache: 'force-cache', next: { tags: [`products:${id}`] } }).catch(() => null)
  if (!product) return { title: "Product not found - CodeDuck Store" }
  return {
    title: `${product.name} - CodeDuck Store`,
    description: product.description,
  }
}

export default async function ProductPage({ params }: ProductPageProps) {
  const { id } = await params

  const productInit: RequestInit = { cache: 'force-cache', next: { tags: [`products:${id}`] } }
  const reviewsInit: RequestInit = { cache: 'force-cache', next: { tags: [`reviews:${id}`] } }

  const [product, reviewPage, installmentPlan] = await Promise.all([
    getProduct(id, productInit).catch(() => null),
    getReviewsByProduct(id, 10, undefined, reviewsInit).catch(() => ({ items: [], nextToken: null })),
    // Synchronous, on-demand calculation (never denormalized onto Product/OpenSearch) — fetched
    // here, alongside the product itself, so the payment-methods modal has real figures at render.
    getInstallmentPlan(id, productInit).catch(() => null),
  ])

  if (!product) notFound()

  const inStock = product.stock > 0

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 lg:px-8">
      <Link
        href={ROUTES.home}
        className="mb-6 inline-flex items-center gap-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        Back to catalog
      </Link>

      <div className="grid gap-8 lg:grid-cols-2 lg:gap-16">
        {/* Product images — main <picture> + thumbnail strip, served straight from the
            image CDN outside the Next optimizer (ADR-0034). */}
        <ProductGallery
          images={product.images}
          name={product.name}
          overlay={
            !inStock ? (
              <div className="absolute inset-0 flex items-center justify-center bg-background/70">
                <Badge variant="secondary" className="text-sm px-4 py-1.5">Out of stock</Badge>
              </div>
            ) : undefined
          }
        />

        {/* Product info */}
        <div className="flex flex-col gap-5">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-foreground lg:text-4xl">{product.name}</h1>
            <div className="mt-2">
              <StarRatingDisplay
                rating={product.averageRating}
                count={product.ratingCount}
                size="md"
              />
            </div>
          </div>

          <p className="text-lg text-muted-foreground leading-relaxed">{product.description}</p>

          <Separator />

          <div className="flex items-start justify-between gap-4">
            <ProductPrice product={product} installmentPlan={installmentPlan} />
            <div className="flex shrink-0 items-center gap-1.5 text-sm">
              <Package className="h-4 w-4 text-muted-foreground" />
              <span className={inStock ? "text-foreground" : "text-muted-foreground"}>
                {inStock ? `${product.stock} in stock` : "Out of stock"}
              </span>
            </div>
          </div>

          <AddToCartButton product={product} />
        </div>
      </div>

      <Separator className="my-12" />

      <ReviewsSection
        productId={product.id}
        initialReviews={reviewPage.items}
        initialNextToken={reviewPage.nextToken}
        averageRating={product.averageRating}
        ratingCount={product.ratingCount}
      />
    </div>
  )
}
