import type { Metadata } from "next"
import { CheckoutView } from "@/features/checkout/components/checkout-view"

// 🔵 SSR: per-user flow (cart/payment) — never cached.
export const dynamic = "force-dynamic"

export const metadata: Metadata = {
  title: "Checkout - CodeDuck Store",
  description: "Securely complete your order of debugging ducks.",
}

export default function CheckoutPage() {
  return <CheckoutView />
}
