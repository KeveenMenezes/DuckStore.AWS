import { Star } from "lucide-react"
import { Progress } from "@/components/ui/progress"
import { cn } from "@/lib/utils"

interface RatingHistogramProps {
  ratingDistribution: { [rating: number]: number }
  ratingCount: number
  className?: string
}

// Each bar's width is the share of that rating within the TOTAL review count (Amazon/Google-style),
// not relative to the largest bucket — a single 1-star outlier shouldn't render as a full-width bar.
export function RatingHistogram({ ratingDistribution, ratingCount, className }: RatingHistogramProps) {
  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      {[5, 4, 3, 2, 1].map((star) => {
        const count = ratingDistribution[star] ?? 0
        const percentage = ratingCount > 0 ? (count / ratingCount) * 100 : 0

        return (
          <div key={star} className="flex items-center gap-2">
            <span className="flex w-8 shrink-0 items-center gap-1 text-sm text-muted-foreground">
              <span className="tabular-nums">{star}</span>
              <Star className="h-3 w-3 fill-muted-foreground/40 text-muted-foreground/40" />
            </span>
            {/* The star label and count sit in sibling elements, which a screen reader never
                associates with the bar itself — without its own name the bar is announced as a
                bare "progressbar". The label repeats both so each row stands alone. */}
            <Progress
              value={percentage}
              className="h-2 flex-1"
              aria-label={`${star} ${star === 1 ? "star" : "stars"}: ${count} of ${ratingCount} reviews`}
            />
            <span className="w-8 shrink-0 text-right text-sm text-muted-foreground">{count}</span>
          </div>
        )
      })}
    </div>
  )
}
