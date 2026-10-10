"use client"

import { useEffect } from "react"
import Link from "next/link"
import { ArrowLeft, Trophy } from "lucide-react"
import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { usePointsBalance } from "@/features/points/hooks/use-points-balance"
import { usePointsHistory } from "@/features/points/hooks/use-points-history"
import type { PointsReason } from "@/features/points/types/points.types"
import { ROUTES } from "@/shared/constants/routes"

const REASON_LABEL: Record<PointsReason, string> = {
  challenge: "Challenge",
  review: "Review",
  redemption: "Redemption",
}

function formatPoints(points: number): string {
  return points > 0 ? `+${points}` : `${points}`
}

/**
 * Points ledger for the signed-in player (ADR-0048 §1). A visitor has no ledger — guests never
 * score (ADR-0045 §7) — so the page sends them to sign in instead of rendering an empty table.
 */
export function PointsHistoryView() {
  const { user, isLoading: authLoading, loginWithCognito } = useAuth()
  const { balance } = usePointsBalance()
  const { entries, isLoading, hasLoaded, error, hasMore, loadMore, retry } = usePointsHistory(
    user !== null,
  )

  const isVisitor = !authLoading && user === null
  useEffect(() => {
    if (isVisitor) loginWithCognito(ROUTES.points)
  }, [isVisitor, loginWithCognito])

  if (authLoading || isVisitor) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-8 lg:px-8">
        <Skeleton className="mb-8 h-9 w-48" />
        <Skeleton className="h-48 w-full" />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-3xl px-4 py-8 lg:px-8">
      <Button asChild variant="ghost" className="mb-6 gap-2 text-muted-foreground">
        <Link href={ROUTES.profile}>
          <ArrowLeft className="h-4 w-4" />
          Back to Profile
        </Link>
      </Button>

      <div className="mb-8 flex flex-wrap items-center justify-between gap-4">
        <h1 className="text-3xl font-bold text-foreground">My Points</h1>
        <div className="flex items-center gap-2 rounded-lg bg-secondary px-4 py-2">
          <Trophy className="h-5 w-5 text-primary" />
          <span className="text-xl font-bold text-foreground">{balance}</span>
          <span className="text-sm text-muted-foreground">pts</span>
        </div>
      </div>

      <Card className="border-border bg-card">
        <CardContent className="p-0">
          {!hasLoaded && isLoading ? (
            <div className="flex flex-col gap-3 p-6">
              {[1, 2, 3].map((n) => (
                <Skeleton key={n} className="h-5 w-full" />
              ))}
            </div>
          ) : hasLoaded && entries.length === 0 && !error ? (
            <div className="flex flex-col items-center gap-3 px-6 py-12 text-center">
              <h2 className="text-lg font-semibold text-foreground">No points yet</h2>
              <p className="text-muted-foreground">
                Solve a code challenge and your points will show up here.
              </p>
              <Button asChild>
                <Link href={ROUTES.challenges}>Go to Challenges</Link>
              </Button>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Reason</TableHead>
                  <TableHead className="text-right">Points</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {entries.map((entry) => (
                  <TableRow key={entry.id}>
                    <TableCell className="text-muted-foreground">
                      {new Date(entry.createdAt).toLocaleString("en-US", {
                        day: "2-digit",
                        month: "short",
                        year: "numeric",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </TableCell>
                    <TableCell>{REASON_LABEL[entry.reason]}</TableCell>
                    <TableCell
                      className={`text-right font-semibold ${entry.points > 0 ? "text-green-600" : "text-foreground"}`}
                    >
                      {formatPoints(entry.points)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {error && (
        <div className="mt-4 flex items-center justify-between gap-4 text-sm text-destructive" role="alert">
          <span>{error}</span>
          <Button variant="outline" size="sm" onClick={retry}>
            Try again
          </Button>
        </div>
      )}

      {hasMore && !error && (
        <div className="mt-4 flex justify-center">
          <Button variant="outline" onClick={loadMore} disabled={isLoading}>
            {isLoading ? "Loading..." : "Load more"}
          </Button>
        </div>
      )}
    </div>
  )
}
