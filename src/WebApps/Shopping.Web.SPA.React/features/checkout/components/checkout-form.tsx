"use client"

import type { FormEvent } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { CreditCard, Wallet } from "lucide-react"
import { Skeleton } from "@/components/ui/skeleton"
import { formatBRL } from "@/shared/lib/format"
import type {
  CheckoutFieldErrors,
  CheckoutFormData,
  CheckoutPaymentMethod,
} from "@/features/checkout/types/checkout.types"
import type { GqlBasketInstallmentPlan } from "@/graphql/types"

interface CheckoutFormProps {
  formData: CheckoutFormData
  errors: CheckoutFieldErrors
  totalPrice: number
  // The unified, cart-level installment plan (null while loading or when the cart is empty).
  installmentPlan: GqlBasketInstallmentPlan | null
  isLoadingInstallmentPlan: boolean
  // True while the user's profile is being fetched to prefill the shipping fields.
  isLoadingProfile: boolean
  onFieldChange: (field: Exclude<keyof CheckoutFormData, "paymentMethod">, value: string) => void
  onPaymentMethodChange: (method: CheckoutPaymentMethod) => void
  onSubmit: (e: FormEvent<HTMLFormElement>) => void | Promise<void>
}

export function CheckoutForm({
  formData,
  errors,
  totalPrice,
  installmentPlan,
  isLoadingInstallmentPlan,
  isLoadingProfile,
  onFieldChange,
  onPaymentMethodChange,
  onSubmit,
}: CheckoutFormProps) {
  const isCash = formData.paymentMethod === "cash"
  // count=1 is the plan's own 1x card price — not returned in `installments` (spec: redundant to
  // repeat), so it's prepended here, same convention as ProductPrice's payment-methods dialog.
  const installmentOptions = installmentPlan
    ? [
        { count: 1, value: installmentPlan.price, hasInterest: false },
        ...installmentPlan.installments,
      ]
    : []
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
            loading={isLoadingProfile}
            onChange={(value) => onFieldChange("name", value)}
          />
          <FormField
            id="email"
            label="Email"
            type="email"
            placeholder="you@email.com"
            value={formData.email}
            error={errors.email}
            loading={isLoadingProfile}
            onChange={(value) => onFieldChange("email", value)}
          />
          <FormField
            id="address"
            label="Address (optional)"
            placeholder="Street, number, complement"
            value={formData.address}
            error={errors.address}
            loading={isLoadingProfile}
            onChange={(value) => onFieldChange("address", value)}
          />
          <FormField
            id="city"
            label="City"
            placeholder="Your city"
            value={formData.city}
            error={errors.city}
            loading={isLoadingProfile}
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
          <RadioGroup
            value={formData.paymentMethod}
            onValueChange={(value) => onPaymentMethodChange(value as CheckoutPaymentMethod)}
            className="grid grid-cols-2 gap-4"
          >
            <Label
              htmlFor="payment-card"
              className="flex cursor-pointer items-center gap-2 rounded-lg border border-border p-3 has-[button[data-state=checked]]:border-primary"
            >
              <RadioGroupItem value="card" id="payment-card" />
              <CreditCard className="h-4 w-4" />
              Card
            </Label>
            <Label
              htmlFor="payment-cash"
              className="flex cursor-pointer items-center gap-2 rounded-lg border border-border p-3 has-[button[data-state=checked]]:border-primary"
            >
              <RadioGroupItem value="cash" id="payment-cash" />
              <Wallet className="h-4 w-4" />
              Cash
            </Label>
          </RadioGroup>

          {isCash ? (
            <p className="text-sm text-muted-foreground">
              Pay in cash — a payment confirmation (QR code) will be generated after you confirm the order.
            </p>
          ) : (
            <>
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
            </>
          )}

          {!isCash && isLoadingInstallmentPlan && (
            <div className="flex flex-col gap-2">
              <Skeleton className="h-4 w-24 animate-none bg-muted skeleton-shimmer" />
              <Skeleton className="h-9 w-full animate-none bg-muted skeleton-shimmer" />
              <span className="text-xs text-muted-foreground">
                Calculating installment options — please wait before confirming your order.
              </span>
            </div>
          )}

          {!isCash && !isLoadingInstallmentPlan && installmentOptions.length > 0 && (
            <div className="flex flex-col gap-2">
              <Label htmlFor="installments" className="text-foreground">
                Installments
              </Label>
              <Select
                value={formData.installments}
                onValueChange={(value) => onFieldChange("installments", value)}
              >
                <SelectTrigger id="installments" className="w-full">
                  <SelectValue placeholder="Select installments" />
                </SelectTrigger>
                <SelectContent>
                  {installmentOptions.map(({ count, value, hasInterest }) => (
                    <SelectItem key={count} value={String(count)}>
                      {count}x of {formatBRL(value)} {hasInterest ? "(with interest)" : "(interest-free)"}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </CardContent>
      </Card>

      <Button
        type="submit"
        size="lg"
        className="gap-2"
        disabled={isLoadingProfile || (!isCash && isLoadingInstallmentPlan)}
      >
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
  loading?: boolean
}

function FormField({
  id,
  label,
  value,
  placeholder,
  onChange,
  error,
  type,
  maxLength,
  loading,
}: FormFieldProps) {
  if (loading) {
    return (
      <div className="flex flex-col gap-2">
        <Skeleton className="h-4 w-24 animate-none bg-muted skeleton-shimmer" />
        <Skeleton className="h-9 w-full animate-none bg-muted skeleton-shimmer" />
      </div>
    )
  }
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
