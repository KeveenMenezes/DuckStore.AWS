"use client"

import { useProductRatingContext } from "@/features/reviews/context/product-rating-context"

/** Public hook for accessing/updating the current product's live rating summary. */
export function useProductRating() {
  return useProductRatingContext()
}
