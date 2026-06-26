"use client"

import { useState, type FormEvent } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Loader2 } from "lucide-react"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { validateRegister } from "@/features/auth/services/auth-validation"

interface RegisterFormProps {
  onError: (message: string) => void
  onSuccess: () => void
}

export function RegisterForm({ onError, onSuccess }: RegisterFormProps) {
  const { register } = useAuth()
  const [form, setForm] = useState({ name: "", email: "", password: "", confirmPassword: "" })
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    onError("")

    const validationError = validateRegister(form)
    if (validationError) {
      onError(validationError)
      return
    }

    setIsSubmitting(true)
    const result = await register(form.name.trim(), form.email.trim(), form.password)
    setIsSubmitting(false)

    if (result.success) {
      onSuccess()
    } else {
      onError(result.error || "Failed to sign up.")
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="reg-name" className="text-foreground">
          Name
        </Label>
        <Input
          id="reg-name"
          value={form.name}
          onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))}
          placeholder="Your name"
          disabled={isSubmitting}
          autoComplete="name"
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="reg-email" className="text-foreground">
          Email
        </Label>
        <Input
          id="reg-email"
          type="email"
          value={form.email}
          onChange={(e) => setForm((p) => ({ ...p, email: e.target.value }))}
          placeholder="you@email.com"
          disabled={isSubmitting}
          autoComplete="email"
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="reg-password" className="text-foreground">
          Password
        </Label>
        <Input
          id="reg-password"
          type="password"
          value={form.password}
          onChange={(e) => setForm((p) => ({ ...p, password: e.target.value }))}
          placeholder="At least 4 characters"
          disabled={isSubmitting}
          autoComplete="new-password"
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="reg-confirm" className="text-foreground">
          Confirm Password
        </Label>
        <Input
          id="reg-confirm"
          type="password"
          value={form.confirmPassword}
          onChange={(e) => setForm((p) => ({ ...p, confirmPassword: e.target.value }))}
          placeholder="Repeat the password"
          disabled={isSubmitting}
          autoComplete="new-password"
        />
      </div>
      <Button type="submit" disabled={isSubmitting} className="gap-2">
        {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
        Sign up
      </Button>
    </form>
  )
}
