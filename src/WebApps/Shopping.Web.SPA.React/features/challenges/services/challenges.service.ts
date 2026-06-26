import { challenges } from "@/features/challenges/data/challenges.data"
import type { Challenge, Language } from "@/features/challenges/types/challenge.types"

export interface ChallengeFilter {
  language: Language | "all"
  difficulty: string
}

/** Return all challenges. */
export function getChallenges(): Challenge[] {
  return challenges
}

/**
 * Filter challenges by language and difficulty ("all" disables a filter).
 * `source` defaults to every challenge but can be a server-fetched list so the
 * client filters the same data the Server Component rendered.
 */
export function filterChallenges(
  { language, difficulty }: ChallengeFilter,
  source: Challenge[] = challenges,
): Challenge[] {
  return source.filter((challenge) => {
    if (language !== "all" && challenge.language !== language) return false
    if (difficulty !== "all" && challenge.difficulty !== difficulty) return false
    return true
  })
}

/** Number of challenges available for a given language. */
export function countByLanguage(language: Language): number {
  return challenges.filter((challenge) => challenge.language === language).length
}

/** Sum of points across every challenge. */
export function getTotalPossiblePoints(): number {
  return challenges.reduce((sum, challenge) => sum + challenge.points, 0)
}
