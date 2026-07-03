"use client"

import { createContext, useContext, useState, useCallback, useEffect, useMemo, type ReactNode } from "react"
import { authService } from "@/features/auth/services/auth.service"
import { ordersService, getOrdersForCustomer } from "@/features/auth/services/orders.service"
import { getGuestCustomerId } from "@/features/cart/services/basket.service"
import type { AuthResult, NewOrderInput, Order, User } from "@/features/auth/types/auth.types"

interface AuthContextType {
  user: User | null
  orders: Order[]
  isLoading: boolean
  ordersLoading: boolean
  login: (email: string, password: string) => Promise<AuthResult>
  register: (name: string, email: string, password: string) => Promise<AuthResult>
  loginWithCognito: () => void
  logout: () => void
  addOrder: (order: NewOrderInput) => void
}

const AuthContext = createContext<AuthContextType | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [ordersLoading, setOrdersLoading] = useState(false)

  // Restore session on mount: check Cognito session first, fall back to localStorage sim.
  useEffect(() => {
    // /api/auth/me always responds 200: { authenticated, user }. "Not logged in"
    // is a valid state (user: null), not an error — so no red console entry on
    // the normal logged-out first visit. Fall back to the localStorage sim when
    // there's no Cognito session.
    fetch('/api/auth/me')
      .then(r => r.json())
      .then((me: { authenticated: boolean; user: { sub: string; email: string; username: string } | null }) => {
        if (me.authenticated && me.user) {
          setUser({ id: me.user.sub, name: me.user.username, email: me.user.email })
        } else {
          const session = authService.getSession()
          if (session) setUser(session)
        }
      })
      .catch(() => {
        const session = authService.getSession()
        if (session) setUser(session)
      })
      .finally(() => setIsLoading(false))

    // Uses the stable guestCustomerId (same value sent at checkout) so the query
    // returns the real orders, regardless of auth state.
    const customerId = getGuestCustomerId()
    setOrdersLoading(true)
    getOrdersForCustomer(customerId)
      .then(setOrders)
      .catch(() => {
        const session = authService.getSession()
        if (session) setOrders(ordersService.getForUser(session.id))
      })
      .finally(() => setOrdersLoading(false))
  }, [])

  const login = useCallback(async (email: string, password: string): Promise<AuthResult> => {
    const result = await authService.login(email, password)
    if (result.success && result.user) {
      setUser(result.user)
      // Orders are already loaded from the backend by the mount effect using the
      // stable guestCustomerId — no need to re-fetch after login.
    }
    return { success: result.success, error: result.error }
  }, [])

  const register = useCallback(async (name: string, email: string, password: string): Promise<AuthResult> => {
    const result = await authService.register(name, email, password)
    if (result.success && result.user) {
      setUser(result.user)
      setOrders([])
    }
    return { success: result.success, error: result.error }
  }, [])

  const loginWithCognito = useCallback(() => {
    window.location.href = '/api/auth/login'
  }, [])

  const logout = useCallback(() => {
    authService.clearSession()
    setUser(null)
    setOrders([])
    // Also clears the Cognito httpOnly cookie via the server logout route
    window.location.href = '/api/auth/logout'
  }, [])

  const addOrder = useCallback(
    (orderData: NewOrderInput) => {
      if (!user) return
      setOrders(ordersService.addForUser(user.id, orderData))
    },
    [user],
  )

  const value = useMemo(
    () => ({ user, orders, isLoading, ordersLoading, login, register, loginWithCognito, logout, addOrder }),
    [user, orders, isLoading, ordersLoading, login, register, loginWithCognito, logout, addOrder],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuthContext() {
  const context = useContext(AuthContext)
  if (!context) throw new Error("useAuth must be used within AuthProvider")
  return context
}
