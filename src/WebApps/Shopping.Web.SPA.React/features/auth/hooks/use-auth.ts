"use client"

import { useAuthContext } from "@/features/auth/context/auth-context"

/** Public entry point for auth state and actions. */
export function useAuth() {
  return useAuthContext()
}
