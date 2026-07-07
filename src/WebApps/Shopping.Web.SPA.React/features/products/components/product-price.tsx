"use client"

import { CreditCard, Wallet } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { formatUSD, percentOff } from "@/shared/lib/format"
import type { Product, InstallmentPlan } from "@/features/products/types/product.types"

interface ProductPriceProps {
  product: Product
  // Fetched separately (installmentPlanFor), computed synchronously by Pricing on every request —
  // never denormalized onto Product/OpenSearch. Null if the fetch failed or no gateway is configured.
  installmentPlan: InstallmentPlan | null
}

/**
 * Price block for the product detail page — the pay-now price (PIX/cash, already reflecting any
 * active campaign discount) up front with its savings badge, the sticker originalPrice struck
 * through underneath, and the interest-free installment perk shown directly below instead of
 * behind a click. A "payment methods" dialog covers the rest (à vista, every installment count
 * including the ones that carry real interest beyond the free tier) for shoppers who want it.
 */
export function ProductPrice({ product, installmentPlan }: ProductPriceProps) {
  const { originalPrice, price, cashPrice } = product
  const maxInstallmentsWithoutInterest =
    installmentPlan?.maxInstallmentsWithoutInterest ?? product.maxInstallmentsWithoutInterest
  const hasCashPerk = cashPrice > 0 && cashPrice < price
  const installments = installmentPlan?.installments ?? []
  const hasInstallments = installments.length > 0

  // count=1 is Product.price itself — installments starts at count=2 (spec: redundant to repeat).
  const rows = [
    { count: 1, value: price, totalValue: price, hasInterest: false },
    ...installments,
  ]
  // The exact backend-computed value at maxInstallmentsWithoutInterest — never divided from price
  // client-side.
  const freeMaxEntry = rows.find((r) => r.count === maxInstallmentsWithoutInterest)

  const payNowPrice = hasCashPerk ? cashPrice : price
  const savingsPercent = percentOff(originalPrice, payNowPrice)

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline gap-2">
        <span className="text-4xl font-bold text-primary">{formatUSD(payNowPrice)}</span>
        {savingsPercent > 0 && (
          <span className="rounded bg-primary/10 px-1.5 py-0.5 text-xs font-semibold text-primary">
            Save {savingsPercent}%
          </span>
        )}
      </div>

      {originalPrice > payNowPrice && (
        <span className="text-sm text-muted-foreground line-through">Reg. {formatUSD(originalPrice)}</span>
      )}

      {maxInstallmentsWithoutInterest > 1 && freeMaxEntry && (
        <span className="text-sm text-muted-foreground">
          Or up to {maxInstallmentsWithoutInterest} interest-free payments of {formatUSD(freeMaxEntry.value)}
        </span>
      )}

      <Dialog>
        <DialogTrigger asChild>
          <Button variant="outline" size="sm" className="mt-1 w-fit gap-1.5">
            <Wallet className="h-3.5 w-3.5" />
            View payment methods
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Payment methods</DialogTitle>
          </DialogHeader>

          <Tabs defaultValue={hasInstallments ? "installments" : "avista"}>
            <TabsList className="w-full">
              <TabsTrigger value="avista" className="gap-1.5">
                <Wallet className="h-3.5 w-3.5" />
                Cash / bank transfer
              </TabsTrigger>
              <TabsTrigger value="installments" disabled={!hasInstallments} className="gap-1.5">
                <CreditCard className="h-3.5 w-3.5" />
                Credit card
              </TabsTrigger>
            </TabsList>

            <TabsContent value="avista" className="pt-2">
              <div className="flex items-center justify-between rounded-lg border border-border p-3">
                <span className="text-sm text-muted-foreground">Bank transfer</span>
                <span className="text-lg font-semibold text-primary">
                  {formatUSD(hasCashPerk ? cashPrice : price)}
                </span>
              </div>
            </TabsContent>

            <TabsContent value="installments" className="pt-2">
              <div className="max-h-72 overflow-y-auto rounded-lg border border-border">
                {rows.map(({ count, value, totalValue, hasInterest }) => (
                  <div
                    key={count}
                    className="flex items-center justify-between border-b border-border px-3 py-2 text-sm last:border-b-0"
                  >
                    <div className="flex flex-col">
                      <span className="text-foreground">
                        {count}x of {formatUSD(value)}
                      </span>
                      {hasInterest && (
                        <span className="text-xs text-muted-foreground">
                          total {formatUSD(totalValue)}
                        </span>
                      )}
                    </div>
                    <span className="text-muted-foreground">
                      {hasInterest ? "with interest" : "interest-free"}
                    </span>
                  </div>
                ))}
              </div>
            </TabsContent>
          </Tabs>
        </DialogContent>
      </Dialog>
    </div>
  )
}
