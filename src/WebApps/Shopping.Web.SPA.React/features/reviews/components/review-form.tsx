"use client"

import { useState, useTransition } from "react"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import { StarRatingInput } from "@/features/reviews/components/star-rating"
import { createReview } from "@/features/reviews/services/reviews.service"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { GraphQLRequestError } from "@/api"

interface ReviewFormProps {
  productId: string
  // "edit" pre-fills the customer's published review; "create" publishes an Eligible/Deleted row.
  mode?: "create" | "edit"
  initialRating?: number
  initialComment?: string
  onSubmitted: (result: { id: string; userName: string; rating: number; comment: string }) => void
  onCancel?: () => void
}

export function ReviewForm({
  productId,
  mode = "create",
  initialRating = 0,
  initialComment = "",
  onSubmitted,
  onCancel,
}: ReviewFormProps) {
  const { loginWithCognito } = useAuth()
  const [rating, setRating] = useState(initialRating)
  const [comment, setComment] = useState(initialComment)
  const [error, setError] = useState<string | null>(null)
  const [sessionExpired, setSessionExpired] = useState(false)
  const [isPending, startTransition] = useTransition()

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (rating === 0) {
      setError("Please select a rating.")
      return
    }
    setError(null)
    setSessionExpired(false)
    startTransition(async () => {
      try {
        const { id, userName } = await createReview({
          productId,
          rating,
          comment,
        })
        // ISR revalidation for other users happens server-side only, via
        // ReviewCreatedEvent/ReviewUpdatedEvent (DynamoDB Streams -> EventBridge -> the
        // `revalidator` Lambda -> /api/webhooks/revalidate, see
        // sst.config.ts). No client-side trigger here — it would only cover
        // reviews submitted through this exact form, leaving a silent blind
        // spot for reviews created any other way (the same category of bug
        // this project already hit once with Catalog updates never
        // reaching the CDN). The reviewer sees their own review instantly via
        // the parent's local state update.
        onSubmitted({ id, userName, rating, comment })
      } catch (err) {
        // Surface the real GraphQL error instead of a fixed string — an
        // AppSync "Not Authorized" means the Cognito session lapsed, which
        // needs a re-login, not a retry.
        if (err instanceof GraphQLRequestError && /not authorized|unauthorized/i.test(err.message)) {
          setSessionExpired(true)
          setError("Your session has expired. Please sign in again to leave a review.")
        } else if (err instanceof GraphQLRequestError) {
          setError(err.message)
        } else {
          setError("Failed to submit review. Please try again.")
        }
      }
    })
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold text-foreground">
        {mode === "edit" ? "Edit Your Review" : "Write a Review"}
      </h3>

      <div className="flex flex-col gap-1.5">
        <Label className="text-xs text-muted-foreground">Your Rating</Label>
        <StarRatingInput value={rating} onChange={setRating} disabled={isPending} />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="review-comment" className="text-xs text-muted-foreground">
          Comment <span className="text-muted-foreground/60">(optional)</span>
        </Label>
        <Textarea
          id="review-comment"
          placeholder="Share your experience with this duck..."
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          disabled={isPending}
          className="min-h-[80px] resize-none text-sm"
          maxLength={500}
        />
      </div>

      {error && (
        <p className="text-xs text-destructive">
          {error}
          {sessionExpired && (
            <>
              {" "}
              <button
                type="button"
                onClick={() => loginWithCognito(window.location.pathname)}
                className="font-medium underline underline-offset-2"
              >
                Sign in
              </button>
            </>
          )}
        </p>
      )}

      <div className="flex justify-end gap-2">
        {onCancel && (
          <Button type="button" variant="ghost" size="sm" disabled={isPending} onClick={onCancel}>
            Cancel
          </Button>
        )}
        <Button type="submit" size="sm" disabled={isPending}>
          {isPending ? "Submitting..." : mode === "edit" ? "Save Changes" : "Submit Review"}
        </Button>
      </div>
    </form>
  )
}
