export interface CheckoutFormData {
  name: string
  email: string
  address: string
  city: string
  cardNumber: string
  cardExpiry: string
  cardCvc: string
}

/** Field-keyed validation messages for the checkout form. */
export type CheckoutFieldErrors = Partial<Record<keyof CheckoutFormData, string>>

/** The checkout flow's state machine. */
export type CheckoutState = "form" | "processing" | "success"
