"use client"

import { useEffect, useRef } from "react"
import { ReviewCard } from "@/features/reviews/components/review-card"
import type { Review } from "@/features/reviews/types/review.types"

interface ReviewListProps {
  reviews: Review[]
  nextToken: string | null
  isPending: boolean
  onLoadMore: () => void
  scrollContainerRef: React.RefObject<HTMLDivElement | null>
}

export function ReviewList({ reviews, nextToken, isPending, onLoadMore, scrollContainerRef }: ReviewListProps) {
  const sentinelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!nextToken) return
    const sentinel = sentinelRef.current
    if (!sentinel) return

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && !isPending) onLoadMore()
      },
      { root: scrollContainerRef.current, rootMargin: "200px" },
    )

    observer.observe(sentinel)
    return () => observer.disconnect()
  }, [nextToken, isPending, onLoadMore, scrollContainerRef])

  // Only reviews with a written comment are shown — rating-only rows still
  // count toward the aggregate but add nothing to the list.
  const withComment = reviews.filter((review) => review.comment?.trim())

  if (withComment.length === 0 && !nextToken) {
    return (
      <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-border py-12 text-center">
        <p className="text-sm text-muted-foreground">No reviews yet. Be the first to share your experience!</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      {withComment.map((review) => (
        <ReviewCard key={review.id} review={review} />
      ))}
      {nextToken && (
        <div ref={sentinelRef} className="flex justify-center py-3">
          {isPending && <span className="text-sm text-muted-foreground">Loading more reviews...</span>}
        </div>
      )}
    </div>
  )
}
