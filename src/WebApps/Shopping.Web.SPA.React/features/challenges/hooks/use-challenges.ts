"use client"

import { useMemo, useState } from "react"
import {
  filterChallenges,
  getChallenges,
} from "@/features/challenges/services/challenges.service"
import type { Challenge, Language } from "@/features/challenges/types/challenge.types"

/**
 * Owns the challenge language/difficulty filter state and exposes the
 * derived list plus catalog-level stats. Keeps filtering logic out of the page.
 * Optionally seeds from server-fetched data (ISR) so the hydrated client
 * filters exactly the list the Server Component rendered.
 */
export function useChallenges(initialChallenges?: Challenge[]) {
  const [selectedLanguage, setSelectedLanguage] = useState<Language | "all">("all")
  const [selectedDifficulty, setSelectedDifficulty] = useState<string>("all")

  const allChallenges = useMemo(
    () => initialChallenges ?? getChallenges(),
    [initialChallenges],
  )
  const totalPossiblePoints = useMemo(
    () => allChallenges.reduce((sum, challenge) => sum + challenge.points, 0),
    [allChallenges],
  )

  const filteredChallenges = useMemo(
    () =>
      filterChallenges(
        { language: selectedLanguage, difficulty: selectedDifficulty },
        allChallenges,
      ),
    [selectedLanguage, selectedDifficulty, allChallenges],
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
    totalChallenges: allChallenges.length,
    totalPossiblePoints,
    resetFilters,
  }
}
