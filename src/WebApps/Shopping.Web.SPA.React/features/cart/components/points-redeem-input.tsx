"use client"

import { Trophy } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { formatBRL } from "@/shared/lib/format"
import { pointsToCurrency, useRewardConversion } from "@/features/points/hooks/use-reward-conversion"

interface PointsRedeemInputProps {
  balance: number
  /** min(balance, the cart's maxRedeemablePoints) — the cap itself is Pricing's (ADR-0048 §3). */
  max: number
  value: number
  onChange: (points: number) => void
  /** Shown when the field was adjusted for the customer (e.g. the cap dropped after a re-quote). */
  notice: string | null
  disabled?: boolean
}

/**
 * "Use points" field for a signed-in customer (ADR-0048 §3). It only collects
 * a number within [0, max]; the conversion shown here is a preview — the discount applied is the
 * quote's, and the server rejects anything above the cap rather than trimming it.
 */
export function PointsRedeemInput({ balance, max, value, onChange, notice, disabled }: PointsRedeemInputProps) {
  const clamp = (points: number) => Math.max(0, Math.min(max, Math.floor(points)))
  // Rendered only for a signed-in customer, which is what rewardConversion requires.
  const conversion = useRewardConversion(true)
  const preview = conversion ? ` · ${value} pts = ${formatBRL(pointsToCurrency(value, conversion))}` : ""

  return (
    <div className="flex flex-col gap-2 rounded-md border border-border p-3">
      <div className="flex items-center justify-between gap-2">
        <Label htmlFor="pointsToUse" className="text-foreground">
          Use points
        </Label>
        <span className="flex items-center gap-1 text-xs text-muted-foreground">
          <Trophy className="h-3.5 w-3.5 text-primary" />
          Balance: {balance} pts
        </span>
      </div>
      <div className="flex items-center gap-2">
        <Input
          id="pointsToUse"
          type="number"
          inputMode="numeric"
          min={0}
          max={max}
          step={1}
          value={value === 0 ? "" : value}
          placeholder="0"
          disabled={disabled || max === 0}
          onChange={(e) => onChange(clamp(Number(e.target.value) || 0))}
          aria-describedby="pointsToUse-help"
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || max === 0 || value === max}
          onClick={() => onChange(max)}
        >
          Max
        </Button>
      </div>
      <span id="pointsToUse-help" className="text-xs text-muted-foreground">
        {max === 0
          ? "No points can be used on this order."
          : `Up to ${max} pts on this order${preview}`}
      </span>
      {notice && (
        <span role="status" className="text-xs font-medium text-amber-600">
          {notice}
        </span>
      )}
    </div>
  )
}
