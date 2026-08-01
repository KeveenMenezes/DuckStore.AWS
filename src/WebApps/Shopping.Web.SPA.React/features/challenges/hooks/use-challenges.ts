"use client"

import { useMemo, useState } from "react"
import {
  filterChallenges,
  getTotalPossiblePoints,
} from "@/features/challenges/services/challenges.service"
import type { Challenge, Language } from "@/features/challenges/types/challenge.types"

/**
 * Owns the challenge language/difficulty filter state and exposes the
 * derived list plus catalog-level stats. Keeps filtering logic out of the page.
 * `initialChallenges` is server-fetched (ISR/SSR) — there is no static fallback
 * (ADR-0045 §10): the client filters exactly the list the server rendered.
 */
export function useChallenges(initialChallenges: Challenge[]) {
  const [selectedLanguage, setSelectedLanguage] = useState<Language | "all">("all")
  const [selectedDifficulty, setSelectedDifficulty] = useState<string>("all")

  const totalPossiblePoints = useMemo(
    () => getTotalPossiblePoints(initialChallenges),
    [initialChallenges],
  )

  const filteredChallenges = useMemo(
    () =>
      filterChallenges(
        { language: selectedLanguage, difficulty: selectedDifficulty },
        initialChallenges,
      ),
    [selectedLanguage, selectedDifficulty, initialChallenges],
  )

  const resetFilters = () => {
    setSelectedLanguage("all")
    setSelectedDifficulty("all")
  }

  return {
    selectedLanguage,
    setSelectedLanguage,
    selectedDifficulty,
    setSelectedDifficulty,
    filteredChallenges,
    totalChallenges: initialChallenges.length,
    totalPossiblePoints,
    resetFilters,
  }
}
