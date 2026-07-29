const imageCdn = (process.env.NEXT_PUBLIC_IMAGE_CDN_URL ?? '').replace(/\/$/, '')
const imgSrc = ["'self'", 'data:', 'blob:', imageCdn].filter(Boolean).join(' ')

// `script-src` deliberately allows 'unsafe-inline' and carries no nonce. Every route here is
// prerendered and served from the CloudFront edge, and a nonce has to be unique per response —
// generating one forces dynamic rendering and gives up the CDN cache for the whole site. The
// inline scripts are also not incidental: React streams the RSC payload as a series of inline
// `self.__next_f.push(...)` calls whose hashes change with every page's content, and the theme
// bootstrap must run before paint. So the directives below are the ones that hold without
// breaking the app; XSS defence rests on React's escaping rather than on CSP.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  `img-src ${imgSrc}`,
  "font-src 'self'",
  // The browser only ever talks to the same-origin BFF (/api/graphql). Setting
  // NEXT_PUBLIC_APPSYNC_URL to let it call AppSync directly would need that origin added here.
  "connect-src 'self'",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "object-src 'none'",
  'upgrade-insecure-requests',
].join('; ')

// Applies to everything the server function serves (documents, RSC payloads, route handlers).
// Assets under /_next/static are served straight from S3 by OpenNext and never reach this, which
// is why sst.config.ts also attaches a CloudFront ResponseHeadersPolicy covering the whole
// distribution — these two are intentionally redundant, not duplicated by mistake.
const securityHeaders = [
  { key: 'Content-Security-Policy', value: contentSecurityPolicy },
  { key: 'Strict-Transport-Security', value: 'max-age=63072000; includeSubDomains; preload' },
  { key: 'X-Content-Type-Options', value: 'nosniff' },
  { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
  { key: 'X-Frame-Options', value: 'DENY' },
  { key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=()' },
  { key: 'Cross-Origin-Opener-Policy', value: 'same-origin' },
]

/** @type {import('next').NextConfig} */
const nextConfig = {
  typescript: {
    ignoreBuildErrors: true,
  },
  // Drops the `x-powered-by: Next.js` framework banner from every response.
  poweredByHeader: false,
  async headers() {
    return [{ source: '/:path*', headers: securityHeaders }]
  },
  experimental: {
    // The client-side Router Cache reuses an already-fetched RSC payload for
    // `static`/ISR routes (/, /products/[id]) for 5 minutes by default on soft
    // navigation (<Link>), regardless of server/CDN tag revalidation — there's
    // no mechanism for revalidateTag() to bust an already-mounted client's
    // cache remotely (see docs/adr/0020-migrate-spa-deploy-to-sst.md). Next.js
    // rejects 0 here ("must be >= 30"), so 30s (its own floor, and the same
    // value already used for `dynamic`) is the closest to "always revalidate"
    // this config actually allows.
    staleTimes: {
      dynamic: 30,
      static: 30,
    },
  },
  images: {
    // AVIF/WebP negotiated via the Accept header — modern browsers get a
    // compact modern format, older ones fall back to the source. We do NOT
    // force JPEG; this is what keeps baseline JPEGs off the page.
    formats: ['image/avif', 'image/webp'],
    // Cap the widths/qualities the optimizer will ever generate.
    deviceSizes: [640, 828, 1080, 1200],
    imageSizes: [64, 128, 256],
    qualities: [60, 75, 85],
    // Optimized images are content-addressed by (url,w,q) and effectively
    // immutable, so give them a 1-year cache header instead of Next's 4h
    // default — satisfies WebPageTest's "cache static content" for images with
    // no staleness risk (a changed source would use a new URL).
    minimumCacheTTL: 31536000,
    // This optimizer config applies only to the app's own UI assets (hero, mentor
    // avatar) in /public. Product catalog images bypass the optimizer entirely
    // (next/image `unoptimized`) and are served straight from the product-images/*
    // CloudFront behavior → S3 bucket (ADR-0018) — so no remotePatterns is needed
    // (same-origin path, resolved from /public in dev and from S3 in prod).
  },
}

export default nextConfig
