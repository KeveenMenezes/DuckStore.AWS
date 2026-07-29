/**
 * Deployment-identity helpers, resolved once from the environment.
 *
 * `NEXT_PUBLIC_SITE_URL` / `NEXT_PUBLIC_IMAGE_CDN_URL` / `NEXT_PUBLIC_ENVIRONMENT` are injected per
 * stage by sst.config.ts; the fallbacks here are what `pnpm dev` runs on.
 */

export const siteUrl = (process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3000').replace(
  /\/+$/,
  '',
)

/** Empty in local dev, where product images resolve from /public on the same origin. */
export const imageCdnUrl = (process.env.NEXT_PUBLIC_IMAGE_CDN_URL ?? '').replace(/\/+$/, '')

export const environmentName = process.env.NEXT_PUBLIC_ENVIRONMENT ?? 'development'

/**
 * Only production may be crawled. Every other stage (`dev-duckstore…`, previews) is served on a
 * public domain off the same hosted zone, so without this they compete with production for the
 * same content in search results.
 */
export const isIndexableEnvironment = environmentName === 'production'
