/**
 * SSR-safe localStorage adapter.
 *
 * All persistence in the app goes through this module so that:
 * - access is guarded against running on the server,
 * - JSON (de)serialization and parse errors are handled in one place,
 * - feature services depend on a small interface instead of the raw API.
 */

function isBrowser(): boolean {
  return typeof window !== "undefined"
}

export const storage = {
  /** Read and JSON-parse a value, returning `fallback` when missing or unparseable. */
  get<T>(key: string, fallback: T): T {
    if (!isBrowser()) return fallback
    try {
      const raw = window.localStorage.getItem(key)
      return raw ? (JSON.parse(raw) as T) : fallback
    } catch {
      return fallback
    }
  },

  /** JSON-serialize and persist a value. */
  set<T>(key: string, value: T): void {
    if (!isBrowser()) return
    try {
      window.localStorage.setItem(key, JSON.stringify(value))
    } catch {
      /* storage unavailable / quota exceeded — fail silently */
    }
  },

  /** Remove a stored value. */
  remove(key: string): void {
    if (!isBrowser()) return
    try {
      window.localStorage.removeItem(key)
    } catch {
      /* no-op */
    }
  },

  /**
   * Read a raw (non-JSON) string value. Used for primitives like the theme
   * preference that were historically stored without JSON encoding.
   */
  getRaw(key: string): string | null {
    if (!isBrowser()) return null
    try {
      return window.localStorage.getItem(key)
    } catch {
      return null
    }
  },

  /** Persist a raw (non-JSON) string value. */
  setRaw(key: string, value: string): void {
    if (!isBrowser()) return
    try {
      window.localStorage.setItem(key, value)
    } catch {
      /* no-op */
    }
  },
}
