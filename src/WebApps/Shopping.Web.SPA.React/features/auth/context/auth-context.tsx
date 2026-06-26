"use client"

import { createContext, useContext, useState, useCallback, useEffect, useMemo, type ReactNode } from "react"
import { authService } from "@/features/auth/services/auth.service"
import { ordersService } from "@/features/auth/services/orders.service"
import type { AuthResult, NewOrderInput, Order, User } from "@/features/auth/types/auth.types"

interface AuthContextType {
  user: User | null
  orders: Order[]
  isLoading: boolean
  login: (email: string, password: string) => Promise<AuthResult>
  register: (name: string, email: string, password: string) => Promise<AuthResult>
  logout: () => void
  addOrder: (order: NewOrderInput) => void
}

const AuthContext = createContext<AuthContextType | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [isLoading, setIsLoading] = useState(true)

  // Restore the persisted session (and its orders) on mount.
  useEffect(() => {
    const session = authService.getSession()
    if (session) {
      setUser(session)
      setOrders(ordersService.getForUser(session.id))
    }
    setIsLoading(false)
  }, [])

  const login = useCallback(async (email: string, password: string): Promise<AuthResult> => {
    const result = await authService.login(email, password)
    if (result.success && result.user) {
      setUser(result.user)
      setOrders(ordersService.getForUser(result.user.id))
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

  const logout = useCallback(() => {
    authService.clearSession()
    setUser(null)
    setOrders([])
  }, [])

  const addOrder = useCallback(
    (orderData: NewOrderInput) => {
      if (!user) return
      setOrders(ordersService.addForUser(user.id, orderData))
    },
    [user],
  )

  const value = useMemo(
    () => ({ user, orders, isLoading, login, register, logout, addOrder }),
    [user, orders, isLoading, login, register, logout, addOrder],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuthContext() {
  const context = useContext(AuthContext)
  if (!context) throw new Error("useAuth must be used within AuthProvider")
  return context
}
