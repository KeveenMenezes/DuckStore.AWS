"use client"

import { createContext, useContext, useState, useEffect, useCallback, useMemo, type ReactNode } from "react"
import { storage } from "@/shared/lib/storage"
import { STORAGE_KEYS } from "@/shared/constants/storage-keys"
import type { Theme } from "@/features/theme/types/theme.types"

interface ThemeContextType {
  theme: Theme
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextType | null>(null)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>("dark")
  const [mounted, setMounted] = useState(false)

  // Restore the persisted preference on mount (client-only). localStorage is unavailable during
  // SSR, so this must sync into state after mount rather than via lazy initial state.
  useEffect(() => {
    const stored = storage.getRaw(STORAGE_KEYS.theme) as Theme | null
    if (stored === "light" || stored === "dark") {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- intentional client-only mount sync
      setTheme(stored)
    }
    setMounted(true)
  }, [])

  // Reflect the active theme onto <html> and persist it. Skipped until mounted
  // so the server-rendered default isn't overwritten before hydration.
  useEffect(() => {
    if (!mounted) return
    const root = document.documentElement
    if (theme === "dark") {
      root.classList.add("dark")
      root.classList.remove("light")
    } else {
      root.classList.remove("dark")
      root.classList.add("light")
    }
    storage.setRaw(STORAGE_KEYS.theme, theme)
  }, [theme, mounted])

  const toggleTheme = useCallback(() => {
    setTheme((prev) => (prev === "dark" ? "light" : "dark"))
  }, [])

  const value = useMemo(() => ({ theme, toggleTheme }), [theme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useThemeContext() {
  const context = useContext(ThemeContext)
  if (!context) throw new Error("useTheme must be used within ThemeProvider")
  return context
}
