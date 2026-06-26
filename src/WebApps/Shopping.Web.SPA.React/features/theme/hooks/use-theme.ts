"use client"

import { useThemeContext } from "@/features/theme/context/theme-context"

/** Public hook for accessing theme state and the toggle action. */
export function useTheme() {
  return useThemeContext()
}
