"use client"

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react"

interface RatingSummary {
  averageRating: number
  ratingCount: number
  ratingDistribution: { [rating: number]: number }
}

interface ProductRatingContextType {
  summary: RatingSummary
  // Applies a brand-new review's rating to the summary — bumps the count, recomputes the
  // average, and increments that rating's histogram bucket. Same shape as the backend's
  // ApplyRatingAsync.
  recordNewReview: (rating: number) => void
  // An edit moves the sum by the delta and one histogram bucket to another; the count stays
  // (ApplyRatingUpdateAsync).
  recordReviewEdit: (oldRating: number, newRating: number) => void
  // A delete takes the rating back out, floored at zero (ApplyRatingRemovalAsync).
  recordReviewRemoval: (rating: number) => void
}

const ProductRatingContext = createContext<ProductRatingContextType | null>(null)

interface ProductRatingProviderProps {
  initialAverageRating: number
  initialRatingCount: number
  initialRatingDistribution: { [rating: number]: number }
  children: ReactNode
}

export function ProductRatingProvider({
  initialAverageRating,
  initialRatingCount,
  initialRatingDistribution,
  children,
}: ProductRatingProviderProps) {
  const [summary, setSummary] = useState<RatingSummary>({
    averageRating: initialAverageRating,
    ratingCount: initialRatingCount,
    ratingDistribution: initialRatingDistribution,
  })

  const recordNewReview = useCallback((rating: number) => {
    setSummary((prev) => {
      const ratingCount = prev.ratingCount + 1
      const averageRating = (prev.averageRating * prev.ratingCount + rating) / ratingCount
      const ratingDistribution = {
        ...prev.ratingDistribution,
        [rating]: (prev.ratingDistribution[rating] ?? 0) + 1,
      }
      return { averageRating, ratingCount, ratingDistribution }
    })
  }, [])

  const recordReviewEdit = useCallback((oldRating: number, newRating: number) => {
    setSummary((prev) => {
      if (prev.ratingCount === 0) return prev
      const averageRating = (prev.averageRating * prev.ratingCount - oldRating + newRating) / prev.ratingCount
      const ratingDistribution = {
        ...prev.ratingDistribution,
        [oldRating]: Math.max((prev.ratingDistribution[oldRating] ?? 0) - 1, 0),
      }
      ratingDistribution[newRating] = (ratingDistribution[newRating] ?? 0) + 1
      return { ...prev, averageRating, ratingDistribution }
    })
  }, [])

  const recordReviewRemoval = useCallback((rating: number) => {
    setSummary((prev) => {
      if (prev.ratingCount === 0) return prev
      const ratingCount = prev.ratingCount - 1
      const averageRating = ratingCount === 0 ? 0 : (prev.averageRating * prev.ratingCount - rating) / ratingCount
      const ratingDistribution = {
        ...prev.ratingDistribution,
        [rating]: Math.max((prev.ratingDistribution[rating] ?? 0) - 1, 0),
      }
      return { averageRating, ratingCount, ratingDistribution }
    })
  }, [])

  const value = useMemo(
    () => ({ summary, recordNewReview, recordReviewEdit, recordReviewRemoval }),
    [summary, recordNewReview, recordReviewEdit, recordReviewRemoval],
  )

  return <ProductRatingContext.Provider value={value}>{children}</ProductRatingContext.Provider>
}

export function useProductRatingContext() {
  const context = useContext(ProductRatingContext)
  if (!context) throw new Error("useProductRating must be used within ProductRatingProvider")
  return context
}
