"use client"

import Link from "next/link"
import { Package, ArrowLeft, ShoppingBag } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { formatBRL } from "@/shared/lib/format"
import { ROUTES } from "@/shared/constants/routes"
import type { OrderStatus } from "@/features/auth/types/auth.types"

const statusMap: Record<OrderStatus, { label: string; variant: "secondary" | "default" | "outline" }> = {
  processing: { label: "Processing", variant: "secondary" },
  shipped: { label: "Shipped", variant: "default" },
  delivered: { label: "Delivered", variant: "outline" },
}

/** Per-user order history. Requires a hydrated session (SSR force-dynamic page). */
export function OrdersView() {
  const { user, orders } = useAuth()

  if (!user) {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-4">
        <div className="flex h-16 w-16 items-center justify-center rounded-full bg-secondary">
          <Package className="h-8 w-8 text-muted-foreground" />
        </div>
        <h2 className="text-xl font-semibold text-foreground">Sign in to view your orders</h2>
        <p className="text-center text-muted-foreground">You need to be signed in to access your orders.</p>
        <Link href={ROUTES.home}>
          <Button variant="outline" className="gap-2">
            <ArrowLeft className="h-4 w-4" />
            Back to Store
          </Button>
        </Link>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-4xl px-4 py-8 lg:px-8">
      <Link href={ROUTES.home}>
        <Button variant="ghost" className="mb-6 gap-2 text-muted-foreground">
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Button>
      </Link>

      <h1 className="mb-8 text-3xl font-bold text-foreground">My Orders</h1>

      {orders.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-4 py-16">
          <div className="flex h-16 w-16 items-center justify-center rounded-full bg-secondary">
            <ShoppingBag className="h-8 w-8 text-muted-foreground" />
          </div>
          <h2 className="text-lg font-semibold text-foreground">No orders yet</h2>
          <p className="text-center text-muted-foreground">You haven't made any purchases yet. How about taking a look at the store?</p>
          <Link href={ROUTES.home}>
            <Button className="gap-2">
              <ArrowLeft className="h-4 w-4" />
              Go to Store
            </Button>
          </Link>
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {orders.map((order) => {
            const status = statusMap[order.status]
            const date = new Date(order.date)
            return (
              <Card key={order.id} className="border-border bg-card">
                <CardHeader className="flex flex-row items-center justify-between pb-3">
                  <div className="flex flex-col gap-1">
                    <CardTitle className="font-mono text-sm text-primary">{order.id}</CardTitle>
                    <p className="text-xs text-muted-foreground">
                      {date.toLocaleDateString("en-US", { day: "2-digit", month: "long", year: "numeric" })}
                      {" at "}
                      {date.toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" })}
                    </p>
                  </div>
                  <Badge variant={status.variant}>{status.label}</Badge>
                </CardHeader>
                <CardContent className="flex flex-col gap-3">
                  {order.items.map((item, idx) => (
                    <div key={idx} className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">
                        {item.name} <span className="text-xs">x{item.quantity}</span>
                      </span>
                      <span className="font-medium text-foreground">
                        {formatBRL(item.price * item.quantity)}
                      </span>
                    </div>
                  ))}
                  <div className="mt-2 flex items-center justify-between border-t border-border pt-3">
                    <span className="font-medium text-foreground">Total</span>
                    <span className="text-lg font-bold text-primary">{formatBRL(order.total)}</span>
                  </div>
                </CardContent>
              </Card>
            )
          })}
        </div>
      )}
    </div>
  )
}
