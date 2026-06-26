"use client"

import { useState } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { AlertCircle } from "lucide-react"
import { LoginForm } from "@/features/auth/components/login-form"
import { RegisterForm } from "@/features/auth/components/register-form"

type AuthTab = "login" | "register"

interface AuthModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  initialTab?: AuthTab
}

export function AuthModal({ open, onOpenChange, initialTab = "login" }: AuthModalProps) {
  const [tab, setTab] = useState<AuthTab>(initialTab)
  const [error, setError] = useState("")

  const handleOpenChange = (next: boolean) => {
    if (!next) setError("")
    onOpenChange(next)
  }

  const switchTab = (newTab: AuthTab) => {
    setTab(newTab)
    setError("")
  }

  const closeModal = () => handleOpenChange(false)

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="border-border bg-card sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="text-center text-xl text-foreground">
            {tab === "login" ? "Sign in to your account" : "Create account"}
          </DialogTitle>
        </DialogHeader>

        <div className="flex rounded-lg bg-secondary p-1">
          <TabButton active={tab === "login"} onClick={() => switchTab("login")}>
            Sign in
          </TabButton>
          <TabButton active={tab === "register"} onClick={() => switchTab("register")}>
            Sign up
          </TabButton>
        </div>

        {error && (
          <div className="flex items-center gap-2 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
            <AlertCircle className="h-4 w-4 shrink-0" />
            {error}
          </div>
        )}

        {tab === "login" ? (
          <LoginForm onError={setError} onSuccess={closeModal} />
        ) : (
          <RegisterForm onError={setError} onSuccess={closeModal} />
        )}
      </DialogContent>
    </Dialog>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}

function TabButton({ active, onClick, children }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex-1 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
        active ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground"
      }`}
    >
      {children}
    </button>
  )
}
