/**
 * Formatting helpers shared across features.
 */

/** Format a numeric value as Brazilian Real, e.g. `29.9` -> `"R$ 29,90"`. */
export function formatBRL(value: number): string {
  return `R$ ${value.toFixed(2).replace(".", ",")}`
}

/** Format a numeric value as US Dollars, e.g. `29.9` -> `"$29.90"`. */
export function formatUSD(value: number): string {
  return `$${value.toFixed(2)}`
}

/** Round a savings percentage for display, e.g. `(39.9, 22.05)` -> `45`. */
export function percentOff(originalPrice: number, discountedPrice: number): number {
  if (originalPrice <= 0 || discountedPrice >= originalPrice) return 0
  return Math.round(((originalPrice - discountedPrice) / originalPrice) * 100)
}

/** Derive up to two uppercase initials from a full name, e.g. `"Ana Lima"` -> `"AL"`.
 * Tolerates a missing/empty name (returns "") so a user record without a name
 * can't crash the render with `undefined.split(...)`. */
export function getInitials(name?: string | null): string {
  if (!name) return ""
  return name
    .split(" ")
    .map((part) => part[0])
    .join("")
    .toUpperCase()
    .slice(0, 2)
}
