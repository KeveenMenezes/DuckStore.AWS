import { Loader2 } from "lucide-react"

export function CheckoutProcessing() {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-4">
      <Loader2 className="h-12 w-12 animate-spin text-primary" />
      <h2 className="text-xl font-semibold text-foreground">Processing payment...</h2>
      <p className="text-muted-foreground">Please wait while we confirm your order.</p>
    </div>
  )
}
