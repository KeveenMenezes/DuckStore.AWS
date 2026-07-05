/** @type {import('next').NextConfig} */
const nextConfig = {
  typescript: {
    ignoreBuildErrors: true,
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
