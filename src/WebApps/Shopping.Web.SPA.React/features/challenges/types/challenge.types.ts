export type Language =
  | "python"
  | "javascript"
  | "typescript"
  | "java"
  | "csharp"
  | "cpp"
  | "sql"

export type Difficulty = "easy" | "medium" | "hard"

export interface Challenge {
  id: string
  title: string
  description: string
  difficulty: Difficulty
  language: Language
  code: string
  options: string[]
  correctAnswer: number
  explanation: string
  hints: string[]
  points: number
}
