"use client"

import { useState, type FormEvent } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Loader2 } from "lucide-react"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { validateLogin } from "@/features/auth/services/auth-validation"

interface LoginFormProps {
  onError: (message: string) => void
  onSuccess: () => void
}

export function LoginForm({ onError, onSuccess }: LoginFormProps) {
  const { login } = useAuth()
  const [form, setForm] = useState({ email: "", password: "" })
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    onError("")

    const validationError = validateLogin(form)
    if (validationError) {
      onError(validationError)
      return
    }

    setIsSubmitting(true)
    const result = await login(form.email.trim(), form.password)
    setIsSubmitting(false)

    if (result.success) {
      onSuccess()
    } else {
      onError(result.error || "Failed to sign in.")
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="login-email" className="text-foreground">
          Email
        </Label>
        <Input
          id="login-email"
          type="email"
          value={form.email}
          onChange={(e) => setForm((p) => ({ ...p, email: e.target.value }))}
          placeholder="you@email.com"
          disabled={isSubmitting}
          autoComplete="email"
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="login-password" className="text-foreground">
          Password
        </Label>
        <Input
          id="login-password"
          type="password"
          value={form.password}
          onChange={(e) => setForm((p) => ({ ...p, password: e.target.value }))}
          placeholder="Your password"
          disabled={isSubmitting}
          autoComplete="current-password"
        />
      </div>
      <Button type="submit" disabled={isSubmitting} className="gap-2">
        {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
        Sign in
      </Button>
    </form>
  )
}
