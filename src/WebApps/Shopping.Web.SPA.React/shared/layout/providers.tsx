"use client"

import type { ReactNode } from "react"
import { ThemeProvider } from "@/features/theme/context/theme-context"
import { AuthProvider } from "@/features/auth/context/auth-context"
import { CartProvider } from "@/features/cart/context/cart-context"
import { ScoreProvider } from "@/features/challenges/context/score-context"
import { CartDrawer } from "@/features/cart/components/cart-drawer"
import { Header } from "@/shared/layout/header"
import { Footer } from "@/shared/layout/footer"

export function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <AuthProvider>
        <CartProvider>
          <ScoreProvider>
            <div className="flex min-h-screen flex-col">
              <Header />
              {/* Target of the skip link in app/layout.tsx. */}
              <main id="main-content" className="flex-1">
                {children}
              </main>
              <Footer />
              <CartDrawer />
            </div>
          </ScoreProvider>
        </CartProvider>
      </AuthProvider>
    </ThemeProvider>
  )
}
