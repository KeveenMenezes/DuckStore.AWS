import Link from "next/link"
import { ArrowLeft, CheckCircle2, Package } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { ROUTES } from "@/shared/constants/routes"

export function CheckoutSuccess({ orderId }: { orderId: string }) {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-6 px-4">
      <div className="flex h-20 w-20 items-center justify-center rounded-full bg-accent/20">
        <CheckCircle2 className="h-10 w-10 text-accent" />
      </div>
      <div className="text-center">
        <h2 className="text-2xl font-bold text-foreground">Order Confirmed!</h2>
        <p className="mt-2 text-muted-foreground">Thank you for your purchase. Your duck is on its way!</p>
      </div>
      <Card className="w-full max-w-sm border-border bg-card">
        <CardContent className="flex flex-col items-center gap-2 p-6">
          <Package className="h-6 w-6 text-primary" />
          <span className="text-sm text-muted-foreground">Order number</span>
          <span className="font-mono text-lg font-bold text-primary">{orderId}</span>
        </CardContent>
      </Card>
      <Button asChild className="gap-2">
        <Link href={ROUTES.home}>
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Link>
      </Button>
    </div>
  )
}
