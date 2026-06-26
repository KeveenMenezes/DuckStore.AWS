import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import { formatBRL } from "@/shared/lib/format"
import type { CartItem } from "@/features/cart/types/cart.types"

interface CheckoutOrderSummaryProps {
  items: CartItem[]
  totalItems: number
  totalPrice: number
}

export function CheckoutOrderSummary({ items, totalItems, totalPrice }: CheckoutOrderSummaryProps) {
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
          <div className="flex justify-between text-sm text-muted-foreground">
            <span>Shipping</span>
            <span className="text-accent">Free</span>
          </div>
          <Separator />
          <div className="flex justify-between text-lg font-bold">
            <span className="text-foreground">Total</span>
            <span className="text-primary">{formatBRL(totalPrice)}</span>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
