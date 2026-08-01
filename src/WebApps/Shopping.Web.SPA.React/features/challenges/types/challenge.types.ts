export type Language =
  | "python"
  | "javascript"
  | "typescript"
  | "java"
  | "csharp"
  | "cpp"
  | "sql"

export type Difficulty = "easy" | "medium" | "hard"

// No correct-option index, explanation or hint text here — the server never sends the answer key
// to the browser (ADR-0045 §2, §10). hintCount says how many hints exist, never their text;
// explanation only ever arrives attached to a SubmitAnswerResult, after grading.
export interface Challenge {
  id: string
  title: string
  description: string
  difficulty: Difficulty
  language: Language
  code: string
  options: string[]
  hintCount: number
  points: number
}

export interface ChallengeAttempt {
  questionId: string
  isCorrect: boolean
  selectedOption: number
  hintsRevealed: number
  pointsEarned: number
  answeredAt: string
}

/** The signed-in player's score and KPIs (ADR-0045 §5, §9). Null for a signed-out visitor. */
export interface ChallengeProgress {
  score: number
  completed: number
  correctCount: number
  wrongCount: number
  hintsUsed: number
  currentStreak: number
  lastAnsweredAt: string | null
  byLanguage: Record<string, number>
  attempts: ChallengeAttempt[]
}

export interface SubmitAnswerResult {
  isCorrect: boolean
  pointsEarned: number
  newScore: number
  explanation: string
  /**
   * The option the graded attempt was recorded against, which is not necessarily the one just
   * submitted: answering is one-shot (ADR-0045 §4), so a re-submission is answered with the first
   * attempt verbatim. Always paint the verdict against this, never the local selection.
   */
  selectedOption: number
}

export interface RevealHintResult {
  hint: string
  hintsRevealed: number
  penaltyApplied: number
}
