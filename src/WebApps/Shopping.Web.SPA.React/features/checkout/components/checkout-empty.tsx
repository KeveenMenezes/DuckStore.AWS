import Link from "next/link"
import { ArrowLeft, ShoppingBag } from "lucide-react"
import { Button } from "@/components/ui/button"
import { ROUTES } from "@/shared/constants/routes"

export function CheckoutEmpty() {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-4">
      <div className="flex h-16 w-16 items-center justify-center rounded-full bg-secondary">
        <ShoppingBag className="h-8 w-8 text-muted-foreground" />
      </div>
      <h2 className="text-xl font-semibold text-foreground">Empty cart</h2>
      <p className="text-center text-muted-foreground">Add products to the cart before checking out.</p>
      <Button asChild variant="outline" className="gap-2">
        <Link href={ROUTES.home}>
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Link>
      </Button>
    </div>
  )
}
