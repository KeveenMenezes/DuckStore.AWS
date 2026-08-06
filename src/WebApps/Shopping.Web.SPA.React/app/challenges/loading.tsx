import { Card, CardContent, CardHeader } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"

/** Suspense fallback shown while the SSR /challenges page fetches challenges + progress. */
export default function ChallengesLoading() {
  return (
    <div className="mx-auto max-w-4xl px-4 py-8 lg:px-8">
      <div className="mb-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[1, 2, 3, 4].map((n) => (
          <div key={n} className="rounded-lg border border-border bg-card p-4">
            <Skeleton className="h-4 w-16" />
            <Skeleton className="mt-2 h-7 w-10" />
            <Skeleton className="mt-1 h-3 w-20" />
          </div>
        ))}
      </div>

      <div className="mb-6 flex flex-col gap-2">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-4 w-96" />
      </div>

      <div className="mb-6 flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Skeleton className="h-3 w-16" />
          <div className="flex flex-wrap gap-2">
            {["w-16", "w-24", "w-24", "w-20", "w-24"].map((w, i) => (
              <Skeleton key={i} className={`h-8 ${w} rounded-full`} />
            ))}
          </div>
        </div>
        <div className="flex flex-col gap-2">
          <Skeleton className="h-3 w-20" />
          <div className="flex flex-wrap gap-2">
            {["w-16", "w-20", "w-20", "w-20"].map((w, i) => (
              <Skeleton key={i} className={`h-8 ${w} rounded-full`} />
            ))}
          </div>
        </div>
      </div>

      <div className="flex flex-col gap-6">
        {[1, 2, 3].map((n) => (
          <Card key={n} className="border-border bg-card">
            <CardHeader className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div className="flex flex-col gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Skeleton className="h-5 w-16 rounded-full" />
                  <Skeleton className="h-5 w-20 rounded-full" />
                </div>
                <Skeleton className="h-5 w-56" />
                <Skeleton className="h-4 w-72" />
              </div>
              <Skeleton className="h-8 w-16 rounded-lg" />
            </CardHeader>
            <CardContent className="flex flex-col gap-6">
              <Skeleton className="h-24 w-full rounded-lg" />
              <div className="flex flex-col gap-2">
                <Skeleton className="h-10 w-full rounded-lg" />
                <Skeleton className="h-10 w-full rounded-lg" />
                <Skeleton className="h-10 w-full rounded-lg" />
              </div>
              <Skeleton className="h-9 w-36 rounded-md" />
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  )
}
