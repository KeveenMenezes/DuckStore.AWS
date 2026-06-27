import type { Metadata } from "next"
import { OrdersView } from "@/features/auth/components/orders-view"

// SSG: server shell is identical for all users. Auth and user data hydrate client-side.
export const revalidate = false

export const metadata: Metadata = {
  title: "My Orders - CodeDuck Store",
  description: "Track the status and history of your orders.",
}

export default function MyOrdersPage() {
  return <OrdersView />
}
