import type { MetadataRoute } from 'next'
import { getProducts } from '@/features/products/services/products.service'
import { isIndexableEnvironment, siteUrl } from '@/shared/lib/site'
import { ROUTES } from '@/shared/constants/routes'

// Same ISR contract as app/page.tsx: never TTL-expired, regenerated when the `products` tag is
// invalidated by the revalidator Lambda (which also invalidates /sitemap.xml at the edge).
export const revalidate = false

const staticRoutes: MetadataRoute.Sitemap = [
  { url: `${siteUrl}${ROUTES.home}`, changeFrequency: 'daily', priority: 1 },
  { url: `${siteUrl}${ROUTES.challenges}`, changeFrequency: 'weekly', priority: 0.7 },
]

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  // A non-production stage disallows everything in robots.txt, so enumerating its catalog here
  // would only publish URLs we just asked crawlers to ignore.
  if (!isIndexableEnvironment) return []

  try {
    const products = await getProducts(100, {
      cache: 'force-cache',
      next: { tags: ['products'] },
    })

    return [
      ...staticRoutes,
      ...products.map((product) => ({
        url: `${siteUrl}${ROUTES.product(product.id)}`,
        changeFrequency: 'weekly' as const,
        priority: 0.8,
      })),
    ]
  } catch (error) {
    // The catalog fetch runs at build time against the previous deployment's API. Failing the whole
    // build over a sitemap would be a poor trade — ship the static routes and let the next
    // revalidation fill in the products.
    console.error('Failed to load products for sitemap', error)
    return staticRoutes
  }
}
