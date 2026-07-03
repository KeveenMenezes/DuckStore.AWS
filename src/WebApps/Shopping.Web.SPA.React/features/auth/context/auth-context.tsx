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
  loginWithCognito: () => void
  signUpWithCognito: () => void
  logout: () => void
  addOrder: (order: NewOrderInput) => void
}

const AuthContext = createContext<AuthContextType | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [ordersLoading, setOrdersLoading] = useState(false)

  // Restore the Cognito session on mount. /api/auth/me always responds 200:
  // { authenticated, user }. "Not logged in" is a valid state (user: null), not an error.
  useEffect(() => {
    let cancelled = false
    fetch('/api/auth/me')
      .then(r => r.json())
      .then((me: { authenticated: boolean; user: { sub: string; email: string; username: string; name?: string } | null }) => {
        if (cancelled || !me.authenticated || !me.user) return

        // Prefer the `name` claim (captured at sign-up) for the display name;
        // `username` is a Cognito UUID. Fall back to email so `name` is never
        // undefined downstream (user-dropdown, initials, etc.).
        setUser({ id: me.user.sub, name: me.user.name || me.user.email, email: me.user.email })

        // Orders are Cognito-scoped: ordersByCustomer derives the customer from the token,
        // so the argument is ignored server-side — we only fetch once authenticated.
        setOrdersLoading(true)
        getOrdersForCustomer(me.user.sub)
          .then((o) => { if (!cancelled) setOrders(o) })
          .catch(() => {})
          .finally(() => { if (!cancelled) setOrdersLoading(false) })
      })
      .catch(() => {})
      .finally(() => { if (!cancelled) setIsLoading(false) })

    return () => { cancelled = true }
  }, [])

  const loginWithCognito = useCallback(() => {
    window.location.href = '/api/auth/login'
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
    () => ({ user, orders, isLoading, ordersLoading, loginWithCognito, signUpWithCognito, logout, addOrder }),
    [user, orders, isLoading, ordersLoading, loginWithCognito, signUpWithCognito, logout, addOrder],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuthContext() {
  const context = useContext(AuthContext)
  if (!context) throw new Error("useAuth must be used within AuthProvider")
  return context
}
