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
  // average, and increments that rating's histogram bucket. There is no "previous rating" to
  // account for here (that only applies to edits, which this app doesn't support yet) — this is
  // purely additive, same shape as the backend's ApplyRatingAsync (not ApplyRatingUpdateAsync).
  recordNewReview: (rating: number) => void
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

  const value = useMemo(() => ({ summary, recordNewReview }), [summary, recordNewReview])

  return <ProductRatingContext.Provider value={value}>{children}</ProductRatingContext.Provider>
}

export function useProductRatingContext() {
  const context = useContext(ProductRatingContext)
  if (!context) throw new Error("useProductRating must be used within ProductRatingProvider")
  return context
}
