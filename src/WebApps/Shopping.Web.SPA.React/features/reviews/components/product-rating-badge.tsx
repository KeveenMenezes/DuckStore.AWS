"use client"

import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { useProductRating } from "@/features/reviews/hooks/use-product-rating"

interface ProductRatingBadgeProps {
  size?: "sm" | "md" | "lg"
  className?: string
}

/** Reads live from ProductRatingProvider so a just-submitted review updates this instantly too. */
export function ProductRatingBadge({ size = "md", className }: ProductRatingBadgeProps) {
  const { summary } = useProductRating()
  return (
    <StarRatingDisplay
      rating={summary.averageRating}
      count={summary.ratingCount}
      size={size}
      className={className}
    />
  )
}
