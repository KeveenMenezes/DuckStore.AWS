import type { Difficulty, Language } from "@/features/challenges/types/challenge.types"

// Display-only estimate for the "potential points" shown before a submission — mirrors
// Question.HintPenalty in Challenges.Function, but the real penalty is whatever the server
// actually applies and returns in SubmitAnswerResult.pointsEarned (ADR-0045 §10). Never used to
// compute the score itself.
export const HINT_PENALTY = 25

/** All supported languages, in display order. */
export const ALL_LANGUAGES: Language[] = [
  "python",
  "javascript",
  "typescript",
  "java",
  "csharp",
  "cpp",
  "sql",
]

export const languageLabels: Record<Language, string> = {
  python: "Python",
  javascript: "JavaScript",
  typescript: "TypeScript",
  java: "Java",
  csharp: "C#",
  cpp: "C++",
  sql: "SQL",
}

export const languageColors: Record<Language, string> = {
  python: "bg-[#306998] text-[#FFD43B]",
  javascript: "bg-[#F7DF1E] text-[#323330]",
  typescript: "bg-[#3178C6] text-[#FFFFFF]",
  java: "bg-[#ED8B00] text-[#5382A1]",
  csharp: "bg-[#68217A] text-[#FFFFFF]",
  cpp: "bg-[#00599C] text-[#FFFFFF]",
  sql: "bg-[#CC6699] text-[#FFFFFF]",
}

/** Badge label + styling per difficulty level. */
export const difficultyConfig: Record<Difficulty, { label: string; className: string }> = {
  easy: { label: "Easy", className: "bg-accent/20 text-accent border-accent/30" },
  medium: { label: "Medium", className: "bg-primary/20 text-primary border-primary/30" },
  hard: { label: "Hard", className: "bg-destructive/20 text-destructive border-destructive/30" },
}

/** Difficulty filter options including the "all" sentinel. */
export const DIFFICULTY_FILTERS: ReadonlyArray<{ id: string; label: string }> = [
  { id: "all", label: "All" },
  { id: "easy", label: "Easy" },
  { id: "medium", label: "Medium" },
  { id: "hard", label: "Hard" },
]
