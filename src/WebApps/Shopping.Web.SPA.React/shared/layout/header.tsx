"use client"

import Link from "next/link"
import { ShoppingCart, Code2, Store, Menu, X, Trophy, Sun, Moon, LogIn, UserPlus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useScore } from "@/features/challenges/hooks/use-score"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { useTheme } from "@/features/theme/hooks/use-theme"
import { UserDropdown } from "@/shared/layout/user-dropdown"
import { Skeleton } from "@/components/ui/skeleton"
import { ROUTES } from "@/shared/constants/routes"
import { useState } from "react"

const NEUTRAL_HOVER = "hover:bg-transparent hover:text-foreground dark:hover:bg-transparent"

interface AuthSlotProps {
  isLoading: boolean
  user: unknown
  loginWithCognito: () => void
  signUpWithCognito: () => void
}

function AuthSlot({ isLoading, user, loginWithCognito, signUpWithCognito }: AuthSlotProps) {
  if (isLoading) {
    return (
      <div className="flex items-center gap-2 px-2">
        <Skeleton className="h-7 w-7 rounded-full" />
        <Skeleton className="hidden h-4 w-16 sm:inline-block" />
      </div>
    )
  }

  if (user) {
    return <UserDropdown />
  }

  return (
    <div className="flex items-center gap-1">
      <Button
        variant="ghost"
        size="sm"
        className={`gap-1.5 text-muted-foreground ${NEUTRAL_HOVER}`}
        onClick={() => loginWithCognito()}
      >
        <LogIn className="h-4 w-4" />
        Sign in
      </Button>
      {/* 480px, not `sm`: both buttons fit from ~470px up, so `sm` dropped Sign up while there was
          still room. Below this only real phones remain, where it falls back to the hamburger. */}
      <Button size="sm" className="hidden gap-1.5 min-[480px]:inline-flex" onClick={signUpWithCognito}>
        <UserPlus className="h-4 w-4" />
        Sign up
      </Button>
    </div>
  )
}

export function Header() {
  const { totalItems } = useCart()
  const { score } = useScore()
  const { user, isLoading, loginWithCognito, signUpWithCognito } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

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

          {/* `asChild` renders the Link as the button itself. Nesting a <button> inside an <a>
              instead is invalid HTML and announces as two overlapping controls to screen readers. */}
          <nav className="hidden items-center gap-1 md:flex">
            <Button asChild variant="ghost" className={`gap-2 text-muted-foreground ${NEUTRAL_HOVER}`}>
              <Link href={ROUTES.home}>
                <Store className="h-4 w-4" />
                Store
              </Link>
            </Button>
            <Button asChild variant="ghost" className={`gap-2 text-muted-foreground ${NEUTRAL_HOVER}`}>
              <Link href={ROUTES.challenges}>
                <Code2 className="h-4 w-4" />
                Challenges
              </Link>
            </Button>
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
              className={`text-muted-foreground ${NEUTRAL_HOVER}`}
            >
              {theme === "dark" ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
            </Button>

            <Button
              asChild
              variant="ghost"
              size="icon"
              className={`relative text-muted-foreground ${NEUTRAL_HOVER}`}
              aria-label="Open cart"
            >
              <Link href={ROUTES.cart}>
                <ShoppingCart className="h-5 w-5" />
                {totalItems > 0 && (
                  <span className="absolute -right-1 -top-1 flex h-5 w-5 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground">
                    {totalItems}
                  </span>
                )}
              </Link>
            </Button>

            <AuthSlot
              isLoading={isLoading}
              user={user}
              loginWithCognito={loginWithCognito}
              signUpWithCognito={signUpWithCognito}
            />

            <Button
              variant="ghost"
              size="icon"
              className={`text-muted-foreground md:hidden ${NEUTRAL_HOVER}`}
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
              <Button asChild variant="ghost" className={`w-full justify-start gap-2 text-muted-foreground ${NEUTRAL_HOVER}`}>
                <Link href={ROUTES.home} onClick={() => setMobileMenuOpen(false)}>
                  <Store className="h-4 w-4" />
                  Store
                </Link>
              </Button>
              <Button asChild variant="ghost" className={`w-full justify-start gap-2 text-muted-foreground ${NEUTRAL_HOVER}`}>
                <Link href={ROUTES.challenges} onClick={() => setMobileMenuOpen(false)}>
                  <Code2 className="h-4 w-4" />
                  Challenges
                </Link>
              </Button>
              <div className="flex items-center gap-1.5 rounded-lg bg-secondary px-3 py-1.5 sm:hidden">
                <Trophy className="h-4 w-4 text-primary" />
                <span className="text-sm font-semibold text-foreground">{score} pts</span>
              </div>
              {/* Sign in is always in the header now; only Sign up needs the narrow-width fallback.
                  The breakpoint must mirror the header button's exactly, or Sign up renders twice. */}
              {!isLoading && !user && (
                <div className="flex gap-2 pt-2 min-[480px]:hidden">
                  <Button
                    size="sm"
                    className="flex-1 gap-1.5"
                    onClick={signUpWithCognito}
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
    </>
  )
}
