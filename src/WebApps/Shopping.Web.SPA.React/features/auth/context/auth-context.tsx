"use client"

import { createContext, useContext, useState, useCallback, useEffect, useMemo, type ReactNode } from "react"
import { getOrdersForCustomer } from "@/features/auth/services/orders.service"
import { createOrderId } from "@/shared/lib/id"
import type { NewOrderInput, Order, User } from "@/features/auth/types/auth.types"

interface AuthContextType {
  user: User | null
  orders: Order[]
  isLoading: boolean
  ordersLoading: boolean
  loginWithCognito: (returnTo?: string) => void
  signUpWithCognito: () => void
  logout: () => void
  addOrder: (order: NewOrderInput) => void
  // Re-fetches the authoritative order list from Ordering, replacing any optimistic
  // placeholder addOrder() appended. Optional id override for the just-restored-session case,
  // where customerId hasn't finished propagating into state yet.
  refreshOrders: (customerId?: string) => Promise<void>
}

const AuthContext = createContext<AuthContextType | null>(null)

type MeResponse = {
  authenticated: boolean
  user: { sub: string; email: string; username: string; name?: string } | null
}

/**
 * How hard to try restoring the session, and how long to wait between tries.
 *
 * /api/auth/me answers 5xx for "couldn't determine" — Cognito was throttled or unreachable while
 * renewing the tokens (lib/auth/session.ts). That failure is transient by construction and leaves
 * the session record intact, so retrying recovers a user who is genuinely signed in and would
 * otherwise be rendered as signed out until they happened to reload.
 *
 * Bounded and short on purpose: the header renders a skeleton until this settles, so every retry
 * is paid in perceived page load. Three attempts spend at most ~0.9s waiting before giving up.
 */
const SESSION_RESTORE_ATTEMPTS = 3
const SESSION_RESTORE_BACKOFF_MS = 300

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * Resolves the current session, retrying only the answer that carries no information.
 * Returns null when there is definitively no session, or when the attempts ran out.
 */
async function restoreSession(isCancelled: () => boolean): Promise<MeResponse | null> {
  for (let attempt = 1; attempt <= SESSION_RESTORE_ATTEMPTS; attempt++) {
    if (isCancelled()) return null

    try {
      const res = await fetch('/api/auth/me')

      // Only 5xx is worth retrying. A 2xx is already the answer — including `authenticated: false`,
      // which is a fact about the user, not a failure — and a 4xx won't change on a second ask.
      if (!res.ok) {
        if (res.status < 500) return null
        throw new Error(`/api/auth/me responded ${res.status}`)
      }

      return (await res.json()) as MeResponse
    } catch (error) {
      if (attempt === SESSION_RESTORE_ATTEMPTS) {
        console.error('Failed to restore Cognito session from /api/auth/me', error)
        return null
      }

      await delay(SESSION_RESTORE_BACKOFF_MS * attempt)
    }
  }

  return null
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [ordersLoading, setOrdersLoading] = useState(false)
  const [customerId, setCustomerId] = useState<string | null>(null)

  // Orders are Cognito-scoped: ordersByCustomer derives the customer from the token, so
  // customerId is only used as a cache key here, never sent as a trusted argument.
  const refreshOrders = useCallback(async (id?: string) => {
    const targetId = id ?? customerId
    if (!targetId) return
    setOrdersLoading(true)
    try {
      const o = await getOrdersForCustomer(targetId)
      setOrders(o)
    } catch (error) {
      console.error('Failed to fetch orders for customer', error)
    } finally {
      setOrdersLoading(false)
    }
  }, [customerId])

  // Restore the Cognito session on mount. "Not logged in" is a valid state (user: null), not an
  // error; a transient failure to reach Cognito is retried rather than shown as signed out —
  // see restoreSession above.
  useEffect(() => {
    let cancelled = false

    void restoreSession(() => cancelled).then((me) => {
      if (cancelled) return

      if (me?.authenticated && me.user) {
        // Prefer the `name` claim (captured at sign-up) for the display name;
        // `username` is a Cognito UUID. Fall back to email so `name` is never
        // undefined downstream (user-dropdown, initials, etc.).
        setUser({ id: me.user.sub, name: me.user.name || me.user.email, email: me.user.email })
        setCustomerId(me.user.sub)
        void refreshOrders(me.user.sub)
      }

      setIsLoading(false)
    })

    return () => { cancelled = true }
  }, [])

  const loginWithCognito = useCallback((returnTo?: string) => {
    window.location.href = returnTo
      ? `/api/auth/login?next=${encodeURIComponent(returnTo)}`
      : '/api/auth/login'
  }, [])

  const signUpWithCognito = useCallback(() => {
    window.location.href = '/api/auth/login?screen=signup'
  }, [])

  const logout = useCallback(() => {
    setUser(null)
    setOrders([])
    // Clears the Cognito httpOnly cookies server-side, then redirects to the Cognito logout.
    window.location.href = '/api/auth/logout'
  }, [])

  // Optimistic, in-memory append so the just-placed order shows immediately. The real order
  // is created asynchronously by the Ordering service and replaces this on the next fetch.
  const addOrder = useCallback((orderData: NewOrderInput) => {
    setOrders((prev) => [
      { ...orderData, id: createOrderId(), date: new Date().toISOString(), status: "processing" },
      ...prev,
    ])
  }, [])

  const value = useMemo(
    () => ({
      user, orders, isLoading, ordersLoading, loginWithCognito, signUpWithCognito, logout, addOrder,
      refreshOrders,
    }),
    [
      user, orders, isLoading, ordersLoading, loginWithCognito, signUpWithCognito, logout, addOrder,
      refreshOrders,
    ],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuthContext() {
  const context = useContext(AuthContext)
  if (!context) throw new Error("useAuth must be used within AuthProvider")
  return context
}
