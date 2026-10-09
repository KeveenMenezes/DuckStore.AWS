"use client"

import { useState, useTransition } from "react"
import { Pencil, Trash2 } from "lucide-react"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { StarRatingDisplay } from "@/features/reviews/components/star-rating"
import { deleteReview, formatReviewDate } from "@/features/reviews/services/reviews.service"
import { GraphQLRequestError } from "@/api"
import type { MyReview } from "@/features/reviews/types/review.types"

interface MyReviewCardProps {
  review: MyReview
  onEdit: () => void
  onDeleted: () => void
}

// The signed-in customer's own published review, with Edit and Delete. Deleting keeps the row
// (Deleted) so the form comes back and the customer can publish again until its TTL (ADR-0049).
export function MyReviewCard({ review, onEdit, onDeleted }: MyReviewCardProps) {
  const [error, setError] = useState<string | null>(null)
  const [isPending, startTransition] = useTransition()

  const handleDelete = () => {
    setError(null)
    startTransition(async () => {
      try {
        await deleteReview(review.productId)
        onDeleted()
      } catch (err) {
        setError(err instanceof GraphQLRequestError ? err.message : "Failed to delete review. Please try again.")
      }
    })
  }

  return (
    <Card className="border-border bg-card">
      <CardContent className="flex flex-col gap-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex flex-col gap-1">
            <h3 className="text-sm font-semibold text-foreground">Your Review</h3>
            <div className="flex items-center gap-2">
              <StarRatingDisplay rating={review.rating} size="sm" />
              <span className="text-xs text-muted-foreground">{formatReviewDate(review.createdAt)}</span>
            </div>
          </div>
          <div className="flex shrink-0 gap-2">
            <Button variant="outline" size="sm" onClick={onEdit} disabled={isPending}>
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button variant="outline" size="sm" disabled={isPending}>
                  <Trash2 className="h-3.5 w-3.5" />
                  {isPending ? "Deleting..." : "Delete"}
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Delete your review?</AlertDialogTitle>
                  <AlertDialogDescription>
                    It will be removed from the product page. You can publish a new review for this
                    product within the next 5 days.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Cancel</AlertDialogCancel>
                  <AlertDialogAction onClick={handleDelete}>Delete</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>
        {review.comment?.trim() && (
          <p className="text-sm leading-relaxed text-muted-foreground">{review.comment}</p>
        )}
        {error && <p className="text-xs text-destructive">{error}</p>}
      </CardContent>
    </Card>
  )
}
