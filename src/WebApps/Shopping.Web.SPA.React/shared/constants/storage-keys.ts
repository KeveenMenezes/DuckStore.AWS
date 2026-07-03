/**
 * Single source of truth for localStorage keys.
 * Keeping these centralized prevents typos and key drift across features.
 */
export const STORAGE_KEYS = {
  theme: "codeduck_theme",
} as const
