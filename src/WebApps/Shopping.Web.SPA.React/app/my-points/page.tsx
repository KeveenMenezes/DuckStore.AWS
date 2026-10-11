import type { Metadata } from "next"
import { PointsHistoryView } from "@/features/points/components/points-history-view"

// SSG: server shell is identical for all users. Auth and the ledger hydrate client-side; a visitor
// is sent to sign in by the view.
export const revalidate = false

export const metadata: Metadata = {
  title: "My Points - CodeDuck Store",
  description: "Your points balance and history.",
}

export default function MyPointsPage() {
  return <PointsHistoryView />
}
