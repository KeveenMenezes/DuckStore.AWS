import { Card, CardContent } from "@/components/ui/card"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { formatReviewDate } from "@/features/reviews/services/reviews.service"
import { getInitials } from "@/shared/lib/format"
import type { Review } from "@/features/reviews/types/review.types"

export function ReviewCard({ review }: { review: Review }) {
  return (
    <Card className="border-border bg-card">
      <CardContent className="flex flex-col gap-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-secondary text-sm font-semibold text-foreground">
              {getInitials(review.userName)}
            </div>
            <div>
              <p className="text-sm font-semibold text-foreground">{review.userName}</p>
              <p className="text-xs text-muted-foreground">{formatReviewDate(review.createdAt)}</p>
            </div>
          </div>
          <StarRatingDisplay rating={review.rating} size="sm" />
        </div>
        {review.comment && (
          <p className="text-sm text-muted-foreground leading-relaxed">{review.comment}</p>
        )}
      </CardContent>
    </Card>
  )
}
