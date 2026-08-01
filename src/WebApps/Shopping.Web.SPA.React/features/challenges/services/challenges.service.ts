import { gql, gqlPublic } from "@/api"
import { GET_CHALLENGES, GET_MY_CHALLENGE_PROGRESS } from "@/api/queries/challenges"
import { SUBMIT_CHALLENGE_ANSWER, REVEAL_CHALLENGE_HINT } from "@/api/mutations/challenges"
import type {
  GqlChallenge,
  GqlChallengePage,
  GqlChallengeProgress,
  GqlSubmitChallengeAnswerResult,
  GqlRevealChallengeHintResult,
} from "@/graphql/types"
import type {
  Challenge,
  ChallengeProgress,
  Difficulty,
  Language,
  RevealHintResult,
  SubmitAnswerResult,
} from "@/features/challenges/types/challenge.types"

export interface ChallengeFilter {
  language: Language | "all"
  difficulty: string
}

const DIFFICULTY_FROM_GQL: Record<string, Difficulty> = {
  EASY: "easy",
  MEDIUM: "medium",
  HARD: "hard",
}

/**
 * Anti-corruption boundary: translates the wire-shaped `GqlChallenge` into the domain `Challenge`
 * type the rest of the app consumes — see products.service.ts's toProduct for why this stays
 * field-by-field even where the shapes look identical today.
 */
function toChallenge(gqlChallenge: GqlChallenge): Challenge {
  return {
    id: gqlChallenge.id,
    title: gqlChallenge.title,
    description: gqlChallenge.description,
    difficulty: DIFFICULTY_FROM_GQL[gqlChallenge.difficulty] ?? "easy",
    language: gqlChallenge.language as Language,
    code: gqlChallenge.code,
    options: gqlChallenge.options,
    hintCount: gqlChallenge.hintCount,
    points: gqlChallenge.points,
  }
}

// AWSJSON is opaque per the GraphQL spec — see products.service.ts's parseRatingDistribution for
// why this must handle both the JSON-encoded-string (real AppSync) and native-object (local dev
// backend) shapes.
function parseByLanguage(value: unknown): Record<string, number> {
  if (typeof value === "string") {
    try {
      return JSON.parse(value) as Record<string, number>
    } catch {
      return {}
    }
  }
  return (value as Record<string, number>) ?? {}
}

function toChallengeProgress(gqlProgress: GqlChallengeProgress): ChallengeProgress {
  return {
    score: gqlProgress.score,
    completed: gqlProgress.completed,
    correctCount: gqlProgress.correctCount,
    wrongCount: gqlProgress.wrongCount,
    hintsUsed: gqlProgress.hintsUsed,
    currentStreak: gqlProgress.currentStreak,
    lastAnsweredAt: gqlProgress.lastAnsweredAt,
    byLanguage: parseByLanguage(gqlProgress.byLanguage),
    attempts: gqlProgress.attempts,
  }
}

/** Fetch the public challenge catalog (used by the Server Component page — public, no login). */
export async function getChallenges(pageSize = 100, init?: RequestInit): Promise<Challenge[]> {
  const data = await gqlPublic<{ challenges: GqlChallengePage }>(GET_CHALLENGES, { pageSize }, init)
  return data.challenges.items.map(toChallenge)
}

/**
 * Fetch the signed-in player's score/KPIs (ADR-0045 §5, §7). Cognito-only — callers must not
 * invoke this for a signed-out visitor; the resolver has no API_KEY fallback and would reject it.
 */
export async function getMyChallengeProgress(): Promise<ChallengeProgress> {
  const data = await gql<{ myChallengeProgress: GqlChallengeProgress }>(GET_MY_CHALLENGE_PROGRESS)
  return toChallengeProgress(data.myChallengeProgress)
}

/**
 * Submit an answer for server-side grading (ADR-0045 §3, §4). The client never learns the
 * correct option index, right or wrong — only whether its own selection was correct, the points
 * earned, the resulting total score, and a prose explanation.
 */
export async function submitChallengeAnswer(
  challengeId: string,
  selectedOption: number,
): Promise<SubmitAnswerResult> {
  const data = await gql<{ submitChallengeAnswer: GqlSubmitChallengeAnswerResult }>(
    SUBMIT_CHALLENGE_ANSWER,
    { challengeId, selectedOption },
  )
  return data.submitChallengeAnswer
}

/**
 * Reveal the next hint for a challenge (ADR-0045 §6). The penalty is committed server-side
 * before the hint text is returned — a failed reveal (already answered, or every hint already
 * revealed) throws and no hint text is ever produced.
 */
export async function revealChallengeHint(challengeId: string): Promise<RevealHintResult> {
  const data = await gql<{ revealChallengeHint: GqlRevealChallengeHintResult }>(
    REVEAL_CHALLENGE_HINT,
    { challengeId },
  )
  return data.revealChallengeHint
}

/** Filter challenges by language and difficulty ("all" disables a filter). `source` is required — no static fallback. */
export function filterChallenges(
  { language, difficulty }: ChallengeFilter,
  source: Challenge[],
): Challenge[] {
  return source.filter((challenge) => {
    if (language !== "all" && challenge.language !== language) return false
    if (difficulty !== "all" && challenge.difficulty !== difficulty) return false
    return true
  })
}

/** Number of challenges available for a given language, within an already-loaded list. */
export function countByLanguage(language: Language, source: Challenge[]): number {
  return source.filter((challenge) => challenge.language === language).length
}

/** Sum of points across an already-loaded challenge list. */
export function getTotalPossiblePoints(source: Challenge[]): number {
  return source.reduce((sum, challenge) => sum + challenge.points, 0)
}
