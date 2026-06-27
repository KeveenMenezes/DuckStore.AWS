import type { Metadata } from "next"
import { ChallengesView } from "@/features/challenges/components/challenges-view"
import { getChallenges } from "@/features/challenges/services/challenges.service"

// SSG: challenge list is static data compiled into the bundle — no runtime fetch.
export const revalidate = false

export const metadata: Metadata = {
  title: "Code Challenges - CodeDuck Store",
  description: "Find the bug, fix the code and earn points in interactive challenges across multiple languages.",
}

export default function ChallengesPage() {
  // Server Component provides the list (ISR); per-user score hydrates on the client.
  const challenges = getChallenges()

  return <ChallengesView initialChallenges={challenges} />
}
