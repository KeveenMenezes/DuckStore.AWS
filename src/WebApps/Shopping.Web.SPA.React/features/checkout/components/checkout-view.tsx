"use client"

import Link from "next/link"
import { Button } from "@/components/ui/button"
import { ArrowLeft } from "lucide-react"
import { ROUTES } from "@/shared/constants/routes"
import { useCheckout } from "@/features/checkout/hooks/use-checkout"
import { CheckoutForm } from "@/features/checkout/components/checkout-form"
import { CheckoutOrderSummary } from "@/features/checkout/components/checkout-order-summary"
import { CheckoutEmpty } from "@/features/checkout/components/checkout-empty"
import { CheckoutProcessing } from "@/features/checkout/components/checkout-processing"
import { CheckoutSuccess } from "@/features/checkout/components/checkout-success"

export function CheckoutView() {
  const { state, orderId, formData, errors, items, totalItems, totalPrice, updateField, handleSubmit } = useCheckout()

  if (items.length === 0 && state !== "success") {
    return <CheckoutEmpty />
  }

  if (state === "success") {
    return <CheckoutSuccess orderId={orderId} />
  }

  if (state === "processing") {
    return <CheckoutProcessing />
  }

  return (
    <div className="mx-auto max-w-4xl px-4 py-8 lg:px-8">
      <Link href={ROUTES.home}>
        <Button variant="ghost" className="mb-6 gap-2 text-muted-foreground">
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Button>
      </Link>

      <h1 className="mb-8 text-3xl font-bold text-foreground">Checkout</h1>

      <div className="grid gap-8 lg:grid-cols-5">
        <CheckoutForm
          formData={formData}
          errors={errors}
          totalPrice={totalPrice}
          onFieldChange={updateField}
          onSubmit={handleSubmit}
        />
        <CheckoutOrderSummary items={items} totalItems={totalItems} totalPrice={totalPrice} />
      </div>
    </div>
  )
}
