/**
 * ID generation helpers — single source of truth for app-wide identifiers.
 */

/**
 * Generate a human-readable order id, e.g. `CDK-LXYZ12-AB3C`.
 * Used by both the checkout flow and the orders service.
 */
export function createOrderId(): string {
  const timePart = Date.now().toString(36).toUpperCase()
  const randomPart = Math.random().toString(36).substring(2, 6).toUpperCase()
  return `CDK-${timePart}-${randomPart}`
}

/** Generate a user id, e.g. `user_lxyz12`. */
export function createUserId(): string {
  return `user_${Date.now().toString(36)}`
}
