"use client"

import { useState, type ReactNode } from "react"
import type { ProductImage } from "@/features/products/types/product.types"
import { mainImageId, orderedImages, imageVariantUrl } from "@/shared/lib/image-url"
import { ProductPicture } from "./product-picture"

interface ProductGalleryProps {
  images: ProductImage[]
  name: string
  /** Rendered inside the main image container (e.g. the out-of-stock badge). */
  overlay?: ReactNode
}

/** Main image + clickable thumbnail strip for the product detail page (ADR-0034). */
export function ProductGallery({ images, name, overlay }: ProductGalleryProps) {
  const ordered = orderedImages(images)
  const [selectedId, setSelectedId] = useState<string | null>(mainImageId(images))

  return (
    <div className="flex flex-col gap-3">
      <div className="relative aspect-square overflow-hidden rounded-2xl border border-border bg-card">
        <ProductPicture
          imageId={selectedId}
          alt={name}
          sizes="(max-width: 1024px) 100vw, 600px"
          fallbackWidth={1024}
          className="absolute inset-0 h-full w-full object-cover"
          priority
        />
        {overlay}
      </div>
      {ordered.length > 1 && (
        <div className="flex gap-2 overflow-x-auto">
          {ordered.map((image) => (
            <button
              key={image.imageId}
              type="button"
              onClick={() => setSelectedId(image.imageId)}
              aria-label={`Show image ${image.order + 1} of ${name}`}
              className={`relative h-16 w-16 flex-shrink-0 overflow-hidden rounded-md border ${
                image.imageId === selectedId ? 'border-primary' : 'border-border'
              }`}
            >
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={imageVariantUrl(image.imageId, 160, 'webp')}
                alt=""
                loading="lazy"
                className="h-full w-full object-cover"
              />
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
