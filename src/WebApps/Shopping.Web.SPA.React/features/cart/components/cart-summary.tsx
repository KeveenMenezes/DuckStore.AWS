"use client"

import Link from "next/link"
import { ShoppingBag } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import { formatBRL } from "@/shared/lib/format"
import { ROUTES } from "@/shared/constants/routes"

interface CartSummaryProps {
  totalPrice: number
  isAuthLoading: boolean
  onCheckout: (e: React.MouseEvent<HTMLAnchorElement>) => void
}

export function CartSummary({ totalPrice, isAuthLoading, onCheckout }: CartSummaryProps) {
  return (
    <div className="border-t border-border p-4">
      <div className="flex flex-col gap-2">
        <div className="flex justify-between text-sm text-muted-foreground">
          <span>Subtotal</span>
          <span>{formatBRL(totalPrice)}</span>
        </div>
        <div className="flex justify-between text-sm text-muted-foreground">
          <span>Shipping</span>
          <span className="text-accent">Free</span>
        </div>
        <Separator />
        <div className="flex justify-between text-lg font-bold text-foreground">
          <span>Total</span>
          <span className="text-primary">{formatBRL(totalPrice)}</span>
        </div>
      </div>
      <Link
        href={ROUTES.checkout}
        onClick={onCheckout}
        aria-disabled={isAuthLoading}
        className={isAuthLoading ? "pointer-events-none" : undefined}
      >
        <Button className="mt-4 w-full gap-2" size="lg" disabled={isAuthLoading}>
          <ShoppingBag className="h-4 w-4" />
          Checkout
        </Button>
      </Link>
    </div>
  )
}
