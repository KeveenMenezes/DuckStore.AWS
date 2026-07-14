import type { GqlProductImage } from '@/graphql/types'

// Product image URLs (ADR-0034): the API returns metadata only ({imageId, isMain, order});
// display URLs are built here from the CDN base — swapping bucket/CDN is a config change.
// The processed bucket holds {160,320,640,1024,1600} x {avif,webp,jpg} per imageId,
// all immutable (a replaced image gets a new imageId).

export const IMAGE_VARIANT_WIDTHS = [160, 320, 640, 1024, 1600] as const
export type ImageVariantWidth = (typeof IMAGE_VARIANT_WIDTHS)[number]
export type ImageVariantFormat = 'avif' | 'webp' | 'jpg'

export const IMAGE_PLACEHOLDER = '/icon.svg'

const cdnBase = (process.env.NEXT_PUBLIC_IMAGE_CDN_URL ?? '').replace(/\/+$/, '')

export function imageVariantUrl(
  imageId: string,
  width: ImageVariantWidth,
  format: ImageVariantFormat,
): string {
  return `${cdnBase}/images/${imageId}/${width}.${format}`
}

export function imageSrcSet(imageId: string, format: ImageVariantFormat): string {
  return IMAGE_VARIANT_WIDTHS.map(
    (width) => `${imageVariantUrl(imageId, width, format)} ${width}w`,
  ).join(', ')
}

/** The image to show wherever only one is rendered (cards, cart, order rows). */
export function mainImageId(images: GqlProductImage[]): string | null {
  return (images.find((i) => i.isMain) ?? images[0])?.imageId ?? null
}

export function orderedImages(images: GqlProductImage[]): GqlProductImage[] {
  return [...images].sort((a, b) => a.order - b.order)
}
