/**
 * Centralized application routes.
 * Use these instead of hardcoding path strings across the app.
 */
export const ROUTES = {
  home: "/",
  challenges: "/challenges",
  checkout: "/checkout",
  profile: "/my-profile",
  orders: "/my-orders",
} as const

export type AppRoute = (typeof ROUTES)[keyof typeof ROUTES]
