"use client"

import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { ALL_LANGUAGES, DIFFICULTY_FILTERS, languageLabels } from "@/features/challenges/constants"
import { countByLanguage } from "@/features/challenges/services/challenges.service"
import type { Challenge, Language } from "@/features/challenges/types/challenge.types"

interface ChallengeFiltersProps {
  /** Loaded challenge list — counts are derived from it, not a static import. */
  challenges: Challenge[]
  selectedLanguage: Language | "all"
  onLanguageChange: (language: Language | "all") => void
  selectedDifficulty: string
  onDifficultyChange: (difficulty: string) => void
}

export function ChallengeFilters({
  challenges,
  selectedLanguage,
  onLanguageChange,
  selectedDifficulty,
  onDifficultyChange,
}: ChallengeFiltersProps) {
  return (
    <div className="mb-6 flex flex-col gap-4">
      <div>
        <p className="mb-2 text-sm font-medium text-muted-foreground">Language</p>
        <div className="flex flex-wrap gap-2">
          <Button
            variant={selectedLanguage === "all" ? "default" : "outline"}
            size="sm"
            onClick={() => onLanguageChange("all")}
          >
            All
          </Button>
          {ALL_LANGUAGES.map((lang) => (
            <Button
              key={lang}
              variant={selectedLanguage === lang ? "default" : "outline"}
              size="sm"
              onClick={() => onLanguageChange(lang)}
            >
              {languageLabels[lang]}
              <Badge variant="secondary" className="ml-1 text-xs">
                {countByLanguage(lang, challenges)}
              </Badge>
            </Button>
          ))}
        </div>
      </div>
      <div>
        <p className="mb-2 text-sm font-medium text-muted-foreground">Difficulty</p>
        <div className="flex flex-wrap gap-2">
          {DIFFICULTY_FILTERS.map((d) => (
            <Button
              key={d.id}
              variant={selectedDifficulty === d.id ? "default" : "outline"}
              size="sm"
              onClick={() => onDifficultyChange(d.id)}
            >
              {d.label}
            </Button>
          ))}
        </div>
      </div>
    </div>
  )
}
