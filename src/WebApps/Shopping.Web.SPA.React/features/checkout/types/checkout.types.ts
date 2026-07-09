export type CheckoutPaymentMethod = "card" | "cash"

export interface CheckoutFormData {
  name: string
  email: string
  address: string
  city: string
  // "cash" skips card fields and the installment dropdown entirely — the payment confirmation
  // (QR code) is a separate step, out of scope here.
  paymentMethod: CheckoutPaymentMethod
  cardNumber: string
  cardExpiry: string
  cardCvc: string
  // Selected installment count from the cart-level BasketInstallmentPlan dropdown. Kept as a
  // string like every other form field so it fits the shared FormField/onFieldChange contract;
  // parsed to a number only when submitted.
  installments: string
}

/** Field-keyed validation messages for the checkout form. */
export type CheckoutFieldErrors = Partial<Record<keyof CheckoutFormData, string>>

/** The checkout flow's state machine. */
export type CheckoutState = "form" | "processing" | "success"
