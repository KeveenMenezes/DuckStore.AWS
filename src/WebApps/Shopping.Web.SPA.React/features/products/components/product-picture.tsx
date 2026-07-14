"use client"

import { useState } from "react"
import {
  IMAGE_PLACEHOLDER,
  imageSrcSet,
  imageVariantUrl,
  type ImageVariantWidth,
} from "@/shared/lib/image-url"

interface ProductPictureProps {
  imageId: string | null
  alt: string
  /** The `sizes` hint for responsive selection, e.g. "(max-width: 768px) 50vw, 300px". */
  sizes: string
  /** Fallback <img> width when the browser ignores srcset (also the eager-load target). */
  fallbackWidth?: ImageVariantWidth
  className?: string
  priority?: boolean
}

/**
 * Product image via native <picture> (ADR-0034): AVIF and WebP sources with a JPEG
 * fallback — format negotiation happens in HTML, not at the CDN edge, and everything
 * bypasses the Next image optimizer (ADR-0018's constraint). A product visited seconds
 * after creation may 404 until the processor lands the variants, so errors retry twice
 * (cache-busted, past CloudFront's 5s error TTL) before settling on the placeholder.
 */
export function ProductPicture({
  imageId,
  alt,
  sizes,
  fallbackWidth = 640,
  className,
  priority,
}: ProductPictureProps) {
  const [attempt, setAttempt] = useState(0)

  if (!imageId || attempt > 2) {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={IMAGE_PLACEHOLDER} alt={alt} className={className} />
  }

  const retry = attempt > 0 ? `?retry=${attempt}` : ''

  return (
    <picture key={attempt}>
      <source type="image/avif" srcSet={imageSrcSet(imageId, 'avif')} sizes={sizes} />
      <source type="image/webp" srcSet={imageSrcSet(imageId, 'webp')} sizes={sizes} />
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={`${imageVariantUrl(imageId, fallbackWidth, 'jpg')}${retry}`}
        srcSet={imageSrcSet(imageId, 'jpg')}
        sizes={sizes}
        alt={alt}
        loading={priority ? 'eager' : 'lazy'}
        fetchPriority={priority ? 'high' : undefined}
        className={className}
        onError={() => setTimeout(() => setAttempt((a) => a + 1), 4000)}
      />
    </picture>
  )
}
