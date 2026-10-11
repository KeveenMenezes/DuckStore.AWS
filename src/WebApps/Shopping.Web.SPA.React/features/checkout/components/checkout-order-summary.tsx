import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import { formatBRL } from "@/shared/lib/format"
import { PointsRedeemInput } from "@/features/cart/components/points-redeem-input"
import type { CartItem } from "@/features/cart/types/cart.types"
import type { GqlBasketInstallmentPlan } from "@/graphql/types"

interface CheckoutOrderSummaryProps {
  items: CartItem[]
  totalItems: number
  // The client-side sum of the cart lines, shown only until the quote arrives.
  totalPrice: number
  // The quote's total for the chosen payment method — what will be charged (null while quoting).
  chargedTotal: number | null
  // The server refused the last checkout with PriceChanged: highlight the (re-quoted) total.
  totalChanged: boolean
  // The unified, cart-level installment plan and the count currently selected in CheckoutForm's
  // dropdown (null/1 while the plan is loading or the cart is empty).
  installmentPlan: GqlBasketInstallmentPlan | null
  selectedInstallments: number
  // The "use points" field — null for a visitor, who has no points (ADR-0045 §7).
  points: {
    balance: number
    max: number
    value: number
    notice: string | null
    onChange: (points: number) => void
  } | null
}

export function CheckoutOrderSummary({
  items,
  totalItems,
  totalPrice,
  chargedTotal,
  totalChanged,
  installmentPlan,
  selectedInstallments,
  points,
}: CheckoutOrderSummaryProps) {
  const selectedEntry =
    selectedInstallments === 1
      ? installmentPlan && { count: 1, value: installmentPlan.price }
      : installmentPlan?.installments.find((i) => i.count === selectedInstallments)
  const pointsDiscount = installmentPlan?.pointsDiscount ?? 0
  return (
    <div className="lg:col-span-2">
      <Card className="sticky top-24 border-border bg-card">
        <CardHeader>
          <CardTitle className="text-foreground">Summary ({totalItems} items)</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {items.map((item) => (
            <div key={item.product.id} className="flex justify-between text-sm">
              <span className="text-muted-foreground">
                {item.product.name} x{item.quantity}
              </span>
              <span className="font-medium text-foreground">
                {formatBRL(item.product.price * item.quantity)}
              </span>
            </div>
          ))}
          <Separator />
          {points && (
            <PointsRedeemInput
              balance={points.balance}
              max={points.max}
              value={points.value}
              notice={points.notice}
              onChange={points.onChange}
              disabled={!installmentPlan}
            />
          )}
          {pointsDiscount > 0 && (
            <div className="flex justify-between text-sm">
              <span className="text-muted-foreground">Points</span>
              <span className="font-medium text-green-600">-{formatBRL(pointsDiscount)}</span>
            </div>
          )}
          <div className="flex justify-between text-sm text-muted-foreground">
            <span>Shipping</span>
            <span className="text-accent">Free</span>
          </div>
          <Separator />
          <div
            className={`flex justify-between text-lg font-bold ${totalChanged ? "rounded-md bg-amber-500/10 px-2 py-1 ring-1 ring-amber-500/50" : ""}`}
          >
            <span className="text-foreground">{totalChanged ? "New total" : "Total"}</span>
            <span className="text-primary">{formatBRL(chargedTotal ?? totalPrice)}</span>
          </div>
          {selectedEntry && selectedEntry.count > 1 && (
            <span className="text-right text-sm text-muted-foreground">
              or {selectedEntry.count}x of {formatBRL(selectedEntry.value)}
            </span>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
