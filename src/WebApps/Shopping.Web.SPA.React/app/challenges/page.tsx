import type { Metadata } from "next"
import { ChallengesView } from "@/features/challenges/components/challenges-view"
import { getChallenges } from "@/features/challenges/services/challenges.service"

// 🟡 ISR: challenge list and static catalog content.
export const revalidate = 300

export const metadata: Metadata = {
  title: "Code Challenges - CodeDuck Store",
  description: "Find the bug, fix the code and earn points in interactive challenges across multiple languages.",
}

export default function ChallengesPage() {
  // Server Component provides the list (ISR); per-user score hydrates on the client.
  const challenges = getChallenges()

  return <ChallengesView initialChallenges={challenges} />
}
