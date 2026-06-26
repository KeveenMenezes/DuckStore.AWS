import type { Metadata } from "next"
import { ProfileView } from "@/features/auth/components/profile-view"

// 🔵 SSR: per-user data (session/score) — never cached.
export const dynamic = "force-dynamic"

export const metadata: Metadata = {
  title: "My Profile - CodeDuck Store",
  description: "View your score, completed challenges and placed orders.",
}

export default function MyProfilePage() {
  return <ProfileView />
}
