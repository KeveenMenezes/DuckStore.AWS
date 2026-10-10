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
  const {
    state,
    orderId,
    formData,
    errors,
    checkoutError,
    items,
    totalItems,
    totalPrice,
    chargedTotal,
    installmentPlan,
    isLoadingInstallmentPlan,
    isLoadingProfile,
    isSignedIn,
    balance,
    pointsToUse,
    maxPoints,
    pointsNotice,
    priceChangedTotal,
    updateField,
    updatePaymentMethod,
    updatePointsToUse,
    handleSubmit,
  } = useCheckout()

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
      <Button asChild variant="ghost" className="mb-6 gap-2 text-muted-foreground">
        <Link href={ROUTES.home}>
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Link>
      </Button>

      <h1 className="mb-8 text-3xl font-bold text-foreground">Checkout</h1>

      {priceChangedTotal !== null && (
        <div
          role="alert"
          className="mb-6 rounded-md border border-amber-500/50 bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:text-amber-400"
        >
          Your cart total changed. Review the new total before checking out.
        </div>
      )}

      {checkoutError && (
        <div className="mb-6 rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {checkoutError}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-5">
        <CheckoutForm
          formData={formData}
          errors={errors}
          chargedTotal={chargedTotal}
          installmentPlan={installmentPlan}
          isLoadingInstallmentPlan={isLoadingInstallmentPlan}
          isLoadingProfile={isLoadingProfile}
          onFieldChange={updateField}
          onPaymentMethodChange={updatePaymentMethod}
          onSubmit={handleSubmit}
        />
        <CheckoutOrderSummary
          items={items}
          totalItems={totalItems}
          totalPrice={totalPrice}
          chargedTotal={chargedTotal}
          totalChanged={priceChangedTotal !== null}
          installmentPlan={installmentPlan}
          selectedInstallments={Number(formData.installments) || 1}
          points={
            isSignedIn
              ? { balance, max: maxPoints, value: pointsToUse, notice: pointsNotice, onChange: updatePointsToUse }
              : null
          }
        />
      </div>
    </div>
  )
}
