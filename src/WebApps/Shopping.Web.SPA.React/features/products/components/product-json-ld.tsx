import type { Product } from '@/features/products/types/product.types'
import { imageVariantUrl, orderedImages } from '@/shared/lib/image-url'
import { ROUTES } from '@/shared/constants/routes'
import { siteUrl } from '@/shared/lib/site'

/** The figure a buyer actually pays, mirroring what product-card.tsx and ProductPrice display. */
function payNowPrice(product: Product): number {
  const hasCashPerk = product.cashPrice > 0 && product.cashPrice < product.price
  return hasCashPerk ? product.cashPrice : product.price
}

/**
 * schema.org Product data for the search engines, covering the price and rating this page already
 * has server-side. Emitted as JSON-LD rather than microdata so the markup stays untouched.
 *
 * `aggregateRating` is omitted entirely when nothing has been reviewed yet: Google rejects the
 * whole block for a rating of 0 out of 0 votes, which would cost the rich result outright.
 */
export function ProductJsonLd({ product }: { readonly product: Product }) {
  const url = `${siteUrl}${ROUTES.product(product.id)}`
  const jsonLd = {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: product.name,
    description: product.description,
    sku: product.id,
    image: orderedImages(product.images).map((image) =>
      imageVariantUrl(image.imageId, 1024, 'jpg'),
    ),
    offers: {
      '@type': 'Offer',
      url,
      price: payNowPrice(product).toFixed(2),
      priceCurrency: 'USD',
      availability:
        product.stock > 0
          ? 'https://schema.org/InStock'
          : 'https://schema.org/OutOfStock',
    },
    ...(product.ratingCount > 0
      ? {
          aggregateRating: {
            '@type': 'AggregateRating',
            ratingValue: product.averageRating,
            reviewCount: product.ratingCount,
          },
        }
      : {}),
  }

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
    />
  )
}
