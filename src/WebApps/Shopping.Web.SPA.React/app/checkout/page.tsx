import type { Metadata } from "next"
import { CheckoutView } from "@/features/checkout/components/checkout-view"

// SSG: server shell is identical for all users. Cart and auth hydrate client-side.
export const revalidate = false

export const metadata: Metadata = {
  title: "Checkout - CodeDuck Store",
  description: "Securely complete your order of debugging ducks.",
}

export default function CheckoutPage() {
  return <CheckoutView />
}
