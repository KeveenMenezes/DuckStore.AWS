/**
 * Single source of truth for localStorage keys.
 * Keeping these centralized prevents typos and key drift across features.
 */
export const STORAGE_KEYS = {
  session: "codeduck_session",
  users: "codeduck_users",
  theme: "codeduck_theme",
  orders: (userId: string) => `codeduck_orders_${userId}`,
  guestUsername: "codeduck_guest_username",
} as const
