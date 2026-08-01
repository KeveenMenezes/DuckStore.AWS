import type { Metadata } from "next"
import { getSession } from "@/lib/auth/session"
import { ChallengesView } from "@/features/challenges/components/challenges-view"
import { getChallenges, getMyChallengeProgress } from "@/features/challenges/services/challenges.service"

// SSR: progress is personalized, so this page can't be pre-rendered (ADR-0045 §10, see the
// rendering-strategy skill). getSession() below reads a cookie and would force this dynamic on
// its own, but the directive is kept explicit so the reason survives a future refactor.
export const dynamic = "force-dynamic"

export const metadata: Metadata = {
  title: "Code Challenges - CodeDuck Store",
  description: "Find the bug, fix the code and earn points in interactive challenges across multiple languages.",
}

export default async function ChallengesPage() {
  const session = await getSession()
  const [challenges, progress] = await Promise.all([
    getChallenges(),
    // Cognito-only (ADR-0045 §7) — a signed-out visitor still sees and can try every challenge,
    // just with no score to hydrate.
    session ? getMyChallengeProgress() : Promise.resolve(null),
  ])

  return <ChallengesView initialChallenges={challenges} initialProgress={progress} />
}
