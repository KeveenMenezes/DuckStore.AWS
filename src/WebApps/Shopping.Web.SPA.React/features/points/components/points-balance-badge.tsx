"use client"

import { Trophy } from "lucide-react"
import { usePointsBalance } from "@/features/points/hooks/use-points-balance"
import { cn } from "@/lib/utils"

interface PointsBalanceBadgeProps {
  className?: string
}

/**
 * Header badge with the signed-in player's balance. Renders nothing for a visitor: guests play
 * challenges but never score (ADR-0045 §7), so a "0 pts" badge would only be misleading.
 */
export function PointsBalanceBadge({ className }: PointsBalanceBadgeProps) {
  const { balance, isAuthenticated } = usePointsBalance()

  if (!isAuthenticated) return null

  return (
    <div
      className={cn("flex items-center gap-1.5 rounded-lg bg-secondary px-3 py-1.5", className)}
      aria-label={`${balance} points`}
    >
      <Trophy className="h-4 w-4 text-primary" />
      <span className="text-sm font-semibold text-foreground">{balance}</span>
      <span className="text-xs text-muted-foreground">pts</span>
    </div>
  )
}
