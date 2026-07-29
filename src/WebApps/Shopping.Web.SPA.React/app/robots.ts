import type { MetadataRoute } from 'next'
import { isIndexableEnvironment, siteUrl } from '@/shared/lib/site'

// Without this route /robots.txt is a 404, which leaves crawlers free to index whatever they find.
// That matters most for the non-production stages: they are served on public domains off the same
// hosted zone as production, so they would compete with it for the same content.
export default function robots(): MetadataRoute.Robots {
  if (!isIndexableEnvironment) {
    return { rules: [{ userAgent: '*', disallow: '/' }] }
  }

  return {
    rules: [
      {
        userAgent: '*',
        allow: '/',
        // Personalized or transactional routes: nothing to rank, and /checkout and /cart depend
        // entirely on client state, so a crawler only ever sees an empty shell.
        disallow: ['/api/', '/cart', '/checkout', '/my-profile', '/my-orders'],
      },
    ],
    sitemap: `${siteUrl}/sitemap.xml`,
  }
}
