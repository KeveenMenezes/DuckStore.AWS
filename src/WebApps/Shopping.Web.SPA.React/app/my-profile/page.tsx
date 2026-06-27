import type { Metadata } from "next"
import { ProfileView } from "@/features/auth/components/profile-view"

// SSG: server shell is identical for all users. Auth and user data hydrate client-side.
export const revalidate = false

export const metadata: Metadata = {
  title: "My Profile - CodeDuck Store",
  description: "View your score, completed challenges and placed orders.",
}

export default function MyProfilePage() {
  return <ProfileView />
}
