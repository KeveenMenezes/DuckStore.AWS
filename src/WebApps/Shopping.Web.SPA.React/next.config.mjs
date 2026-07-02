/** @type {import('next').NextConfig} */
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
    // All images are currently local (/public). A future product-images bucket
    // outside the site files will need an images.remotePatterns entry here.
  },
}

export default nextConfig
