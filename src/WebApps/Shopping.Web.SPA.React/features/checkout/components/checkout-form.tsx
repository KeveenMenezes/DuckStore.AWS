"use client"

import type { FormEvent } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { CreditCard } from "lucide-react"
import { formatBRL } from "@/shared/lib/format"
import type { CheckoutFieldErrors, CheckoutFormData } from "@/features/checkout/types/checkout.types"

interface CheckoutFormProps {
  formData: CheckoutFormData
  errors: CheckoutFieldErrors
  totalPrice: number
  onFieldChange: (field: keyof CheckoutFormData, value: string) => void
  onSubmit: (e: FormEvent<HTMLFormElement>) => void | Promise<void>
}

export function CheckoutForm({ formData, errors, totalPrice, onFieldChange, onSubmit }: CheckoutFormProps) {
  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-6 lg:col-span-3">
      <Card className="border-border bg-card">
        <CardHeader>
          <CardTitle className="text-foreground">Shipping Details</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <FormField
            id="name"
            label="Full Name"
            placeholder="Your name"
            value={formData.name}
            error={errors.name}
            onChange={(value) => onFieldChange("name", value)}
          />
          <FormField
            id="email"
            label="Email"
            type="email"
            placeholder="you@email.com"
            value={formData.email}
            error={errors.email}
            onChange={(value) => onFieldChange("email", value)}
          />
          <FormField
            id="address"
            label="Address"
            placeholder="Street, number, complement"
            value={formData.address}
            error={errors.address}
            onChange={(value) => onFieldChange("address", value)}
          />
          <FormField
            id="city"
            label="City"
            placeholder="Your city"
            value={formData.city}
            error={errors.city}
            onChange={(value) => onFieldChange("city", value)}
          />
        </CardContent>
      </Card>

      <Card className="border-border bg-card">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-foreground">
            <CreditCard className="h-5 w-5" />
            Payment (simulated)
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <FormField
            id="cardNumber"
            label="Card Number"
            placeholder="0000 0000 0000 0000"
            maxLength={19}
            value={formData.cardNumber}
            error={errors.cardNumber}
            onChange={(value) => onFieldChange("cardNumber", value)}
          />
          <div className="grid grid-cols-2 gap-4">
            <FormField
              id="cardExpiry"
              label="Expiry"
              placeholder="MM/YY"
              maxLength={5}
              value={formData.cardExpiry}
              error={errors.cardExpiry}
              onChange={(value) => onFieldChange("cardExpiry", value)}
            />
            <FormField
              id="cardCvc"
              label="CVC"
              placeholder="123"
              maxLength={4}
              value={formData.cardCvc}
              error={errors.cardCvc}
              onChange={(value) => onFieldChange("cardCvc", value)}
            />
          </div>
        </CardContent>
      </Card>

      <Button type="submit" size="lg" className="gap-2">
        <CreditCard className="h-4 w-4" />
        Confirm Order - {formatBRL(totalPrice)}
      </Button>
    </form>
  )
}

interface FormFieldProps {
  id: string
  label: string
  value: string
  placeholder: string
  onChange: (value: string) => void
  error?: string
  type?: string
  maxLength?: number
}

function FormField({ id, label, value, placeholder, onChange, error, type, maxLength }: FormFieldProps) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id} className="text-foreground">
        {label}
      </Label>
      <Input
        id={id}
        type={type}
        value={value}
        maxLength={maxLength}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className={error ? "border-destructive" : ""}
      />
      {error && <span className="text-xs text-destructive">{error}</span>}
    </div>
  )
}
