/** @type {import('next').NextConfig} */

// Product images are served from our own bucket through the SPA's CloudFront
// domain (see infra/constructs/spa-product-images.ts). Next's optimizer only
// fetches remote sources whose host is whitelisted here; derive it from the
// site URL injected at build time so it tracks the environment's domain.
const siteHost = process.env.NEXT_PUBLIC_SITE_URL
  ? new URL(process.env.NEXT_PUBLIC_SITE_URL).hostname
  : undefined

const nextConfig = {
  typescript: {
    ignoreBuildErrors: true,
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
    remotePatterns: siteHost
      ? [{ protocol: 'https', hostname: siteHost, pathname: '/product-images/**' }]
      : [],
  },
}

export default nextConfig
