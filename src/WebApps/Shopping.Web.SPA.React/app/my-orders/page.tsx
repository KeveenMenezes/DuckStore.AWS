import type { Metadata } from "next"
import { OrdersView } from "@/features/auth/components/orders-view"

// 🔵 SSR: per-user order history — never cached.
export const dynamic = "force-dynamic"

export const metadata: Metadata = {
  title: "My Orders - CodeDuck Store",
  description: "Track the status and history of your orders.",
}

export default function MyOrdersPage() {
  return <OrdersView />
}
