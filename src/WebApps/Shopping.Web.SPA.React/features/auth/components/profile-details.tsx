"use client"

import { useEffect, useState, type FormEvent } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { getMyProfile, updateProfile } from "@/features/auth/services/profile.service"
import type { UpdateProfileInput } from "@/graphql/types"

type FormState = {
  name: string
  phone: string
  addressLine: string
  city: string
  state: string
  zipCode: string
  country: string
}

const EMPTY: FormState = {
  name: "", phone: "", addressLine: "", city: "", state: "", zipCode: "", country: "",
}

const FIELDS: Array<{ key: keyof FormState; label: string }> = [
  { key: "name", label: "Full name" },
  { key: "phone", label: "Phone" },
  { key: "addressLine", label: "Address" },
  { key: "city", label: "City" },
  { key: "state", label: "State" },
  { key: "zipCode", label: "ZIP code" },
  { key: "country", label: "Country" },
]

/** Editable profile (name + contact + shipping address). Email is Cognito-owned and read-only. */
export function ProfileDetails() {
  const [form, setForm] = useState<FormState>(EMPTY)
  const [email, setEmail] = useState("")
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    let cancelled = false
    getMyProfile()
      .then((p) => {
        if (cancelled) return
        setEmail(p.email)
        setForm({
          name: p.name ?? "",
          phone: p.phone ?? "",
          addressLine: p.addressLine ?? "",
          city: p.city ?? "",
          state: p.state ?? "",
          zipCode: p.zipCode ?? "",
          country: p.country ?? "",
        })
      })
      .catch(() => {})
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  const update = (key: keyof FormState, value: string) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    setSaved(false)
  }

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setSaving(true)
    setSaved(false)
    try {
      // Empty optional fields are sent as null so they aren't stored as blank strings.
      const input: UpdateProfileInput = {
        name: form.name.trim(),
        phone: form.phone.trim() || null,
        addressLine: form.addressLine.trim() || null,
        city: form.city.trim() || null,
        state: form.state.trim() || null,
        zipCode: form.zipCode.trim() || null,
        country: form.country.trim() || null,
      }
      await updateProfile(input)
      setSaved(true)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card className="border-border bg-card">
      <CardHeader>
        <CardTitle className="text-base font-semibold text-foreground">Account details</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="profile-email">Email</Label>
            <Input id="profile-email" value={email} disabled readOnly />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            {FIELDS.map(({ key, label }) => (
              <div key={key} className="flex flex-col gap-2">
                <Label htmlFor={`profile-${key}`}>{label}</Label>
                <Input
                  id={`profile-${key}`}
                  value={form[key]}
                  onChange={(e) => update(key, e.target.value)}
                  disabled={loading}
                />
              </div>
            ))}
          </div>

          <div className="flex items-center gap-3">
            <Button type="submit" disabled={loading || saving}>
              {saving ? "Saving…" : "Save changes"}
            </Button>
            {saved && <span className="text-sm text-muted-foreground">Saved.</span>}
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
