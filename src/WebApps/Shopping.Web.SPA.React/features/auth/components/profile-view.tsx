"use client"

import Link from "next/link"
import { ArrowLeft, User, Trophy, Package, ShieldCheck } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { useScore } from "@/features/challenges/hooks/use-score"
import { getInitials } from "@/shared/lib/format"
import { ROUTES } from "@/shared/constants/routes"
import { ProfileDetails } from "@/features/auth/components/profile-details"

/** Per-user profile experience. Requires a hydrated session (SSR force-dynamic page). */
export function ProfileView() {
  const { user, orders } = useAuth()
  const { score, completedChallenges } = useScore()

  if (!user) {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 px-4">
        <div className="flex h-16 w-16 items-center justify-center rounded-full bg-secondary">
          <User className="h-8 w-8 text-muted-foreground" />
        </div>
        <h2 className="text-xl font-semibold text-foreground">Sign in to view your profile</h2>
        <Button asChild variant="outline" className="gap-2">
          <Link href={ROUTES.home}>
            <ArrowLeft className="h-4 w-4" />
            Back to Store
          </Link>
        </Button>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-2xl px-4 py-8 lg:px-8">
      <Button asChild variant="ghost" className="mb-6 gap-2 text-muted-foreground">
        <Link href={ROUTES.home}>
          <ArrowLeft className="h-4 w-4" />
          Back to Store
        </Link>
      </Button>

      <h1 className="mb-8 text-3xl font-bold text-foreground">My Profile</h1>

      <div className="flex flex-col gap-6">
        <Card className="border-border bg-card">
          <CardContent className="flex items-center gap-4 p-6">
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-primary text-2xl font-bold text-primary-foreground">
              {getInitials(user.name)}
            </div>
            <div>
              <h2 className="text-xl font-bold text-foreground">{user.name}</h2>
              <p className="text-sm text-muted-foreground">{user.email}</p>
            </div>
          </CardContent>
        </Card>

        <div className="grid gap-4 sm:grid-cols-3">
          <Card className="border-border bg-card">
            <CardHeader className="pb-2">
              <CardTitle className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <Trophy className="h-4 w-4 text-primary" />
                Score
              </CardTitle>
            </CardHeader>
            <CardContent>
              <span className="text-2xl font-bold text-foreground">{score}</span>
              <span className="ml-1 text-sm text-muted-foreground">pts</span>
            </CardContent>
          </Card>

          <Card className="border-border bg-card">
            <CardHeader className="pb-2">
              <CardTitle className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <ShieldCheck className="h-4 w-4 text-accent" />
                Challenges
              </CardTitle>
            </CardHeader>
            <CardContent>
              <span className="text-2xl font-bold text-foreground">{completedChallenges.length}</span>
              <span className="ml-1 text-sm text-muted-foreground">completed</span>
            </CardContent>
          </Card>

          <Card className="border-border bg-card">
            <CardHeader className="pb-2">
              <CardTitle className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <Package className="h-4 w-4 text-primary" />
                Orders
              </CardTitle>
            </CardHeader>
            <CardContent>
              <span className="text-2xl font-bold text-foreground">{orders.length}</span>
              <span className="ml-1 text-sm text-muted-foreground">placed</span>
            </CardContent>
          </Card>
        </div>

        <ProfileDetails />
      </div>
    </div>
  )
}
