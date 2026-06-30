"use client"

import Link from "next/link"
import { ShoppingCart, Code2, Store, Menu, X, Trophy, Sun, Moon, LogIn, UserPlus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useScore } from "@/features/challenges/hooks/use-score"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { useTheme } from "@/features/theme/hooks/use-theme"
import { AuthModal } from "@/features/auth/components/auth-modal"
import { UserDropdown } from "@/shared/layout/user-dropdown"
import { ROUTES } from "@/shared/constants/routes"
import { useState } from "react"

export function Header() {
  const { totalItems } = useCart()
  const { score } = useScore()
  const { user, isLoading } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const [authModal, setAuthModal] = useState<{ open: boolean; tab: "login" | "register" }>({ open: false, tab: "login" })

  return (
    <>
      <header className="sticky top-0 z-50 border-b border-border bg-background/80 backdrop-blur-md">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-3 lg:px-8">
          <Link href={ROUTES.home} className="flex items-center gap-2">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary">
              <span className="text-lg font-bold text-primary-foreground">{'🦆'}</span>
            </div>
            <span className="text-lg font-bold text-foreground">
              Code<span className="text-primary">Duck</span>
            </span>
          </Link>

          <nav className="hidden items-center gap-1 md:flex">
            <Link href={ROUTES.home}>
              <Button variant="ghost" className="gap-2 text-muted-foreground hover:text-foreground">
                <Store className="h-4 w-4" />
                Store
              </Button>
            </Link>
            <Link href={ROUTES.challenges}>
              <Button variant="ghost" className="gap-2 text-muted-foreground hover:text-foreground">
                <Code2 className="h-4 w-4" />
                Challenges
              </Button>
            </Link>
          </nav>

          <div className="flex items-center gap-2">
            <div className="hidden items-center gap-1.5 rounded-lg bg-secondary px-3 py-1.5 sm:flex">
              <Trophy className="h-4 w-4 text-primary" />
              <span className="text-sm font-semibold text-foreground">{score}</span>
              <span className="text-xs text-muted-foreground">pts</span>
            </div>

            <Button
              variant="ghost"
              size="icon"
              onClick={toggleTheme}
              aria-label={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
              className="text-muted-foreground hover:text-foreground"
            >
              {theme === "dark" ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            </Button>

            <Link href={ROUTES.cart} aria-label="Open cart">
              <Button variant="ghost" size="icon" className="relative">
                <ShoppingCart className="h-5 w-5" />
                {totalItems > 0 && (
                  <span className="absolute -right-1 -top-1 flex h-5 w-5 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground">
                    {totalItems}
                  </span>
                )}
              </Button>
            </Link>

            {!isLoading && (
              user ? (
                <UserDropdown />
              ) : (
                <div className="hidden items-center gap-1 sm:flex">
                  <Button
                    variant="ghost"
                    size="sm"
                    className="gap-1.5 text-muted-foreground hover:text-foreground"
                    onClick={() => setAuthModal({ open: true, tab: "login" })}
                  >
                    <LogIn className="h-4 w-4" />
                    Sign in
                  </Button>
                  <Button
                    size="sm"
                    className="gap-1.5"
                    onClick={() => setAuthModal({ open: true, tab: "register" })}
                  >
                    <UserPlus className="h-4 w-4" />
                    Sign up
                  </Button>
                </div>
              )
            )}

            <Button
              variant="ghost"
              size="icon"
              className="md:hidden"
              onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
              aria-label="Menu"
            >
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </div>

        {mobileMenuOpen && (
          <div className="border-t border-border bg-background px-4 py-3 md:hidden">
            <nav className="flex flex-col gap-1">
              <Link href={ROUTES.home} onClick={() => setMobileMenuOpen(false)}>
                <Button variant="ghost" className="w-full justify-start gap-2">
                  <Store className="h-4 w-4" />
                  Store
                </Button>
              </Link>
              <Link href={ROUTES.challenges} onClick={() => setMobileMenuOpen(false)}>
                <Button variant="ghost" className="w-full justify-start gap-2">
                  <Code2 className="h-4 w-4" />
                  Challenges
                </Button>
              </Link>
              <div className="flex items-center gap-1.5 rounded-lg bg-secondary px-3 py-1.5 sm:hidden">
                <Trophy className="h-4 w-4 text-primary" />
                <span className="text-sm font-semibold text-foreground">{score} pts</span>
              </div>
              {!isLoading && !user && (
                <div className="flex gap-2 pt-2 sm:hidden">
                  <Button
                    variant="outline"
                    size="sm"
                    className="flex-1 gap-1.5"
                    onClick={() => { setAuthModal({ open: true, tab: "login" }); setMobileMenuOpen(false) }}
                  >
                    <LogIn className="h-4 w-4" />
                    Sign in
                  </Button>
                  <Button
                    size="sm"
                    className="flex-1 gap-1.5"
                    onClick={() => { setAuthModal({ open: true, tab: "register" }); setMobileMenuOpen(false) }}
                  >
                    <UserPlus className="h-4 w-4" />
                    Sign up
                  </Button>
                </div>
              )}
            </nav>
          </div>
        )}
      </header>

      <AuthModal
        open={authModal.open}
        onOpenChange={(open) => setAuthModal(prev => ({ ...prev, open }))}
        initialTab={authModal.tab}
      />
    </>
  )
}
