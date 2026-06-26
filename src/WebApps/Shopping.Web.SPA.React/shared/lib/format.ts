/**
 * Formatting helpers shared across features.
 */

/** Format a numeric value as Brazilian Real, e.g. `29.9` -> `"R$ 29,90"`. */
export function formatBRL(value: number): string {
  return `R$ ${value.toFixed(2).replace(".", ",")}`
}

/** Derive up to two uppercase initials from a full name, e.g. `"Ana Lima"` -> `"AL"`. */
export function getInitials(name: string): string {
  return name
    .split(" ")
    .map((part) => part[0])
    .join("")
    .toUpperCase()
    .slice(0, 2)
}
