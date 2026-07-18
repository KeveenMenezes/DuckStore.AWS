"use client"

import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { getMyProfile } from "@/features/auth/services/profile.service"
import {
  submitCheckout,
  validateCheckoutForm,
  getBasketInstallmentPlan,
} from "@/features/checkout/services/checkout.service"
import type {
  CheckoutFieldErrors,
  CheckoutFormData,
  CheckoutPaymentMethod,
  CheckoutState,
} from "@/features/checkout/types/checkout.types"
import type { GqlBasketInstallmentPlan } from "@/graphql/types"

const EMPTY_FORM: CheckoutFormData = {
  name: "",
  email: "",
  address: "",
  city: "",
  paymentMethod: "card",
  cardNumber: "",
  cardExpiry: "",
  cardCvc: "",
  installments: "1",
}

/**
 * Owns the entire checkout flow: form state, validation, basket persistence,
 * checkout mutation, and the form/processing/success state machine.
 */
export function useCheckout() {
  const { items, totalPrice, totalItems, clearCart } = useCart()
  const { user, addOrder } = useAuth()

  const [state, setState] = useState<CheckoutState>("form")
  const [orderId, setOrderId] = useState("")
  const [checkoutError, setCheckoutError] = useState<string | null>(null)
  const [formData, setFormData] = useState<CheckoutFormData>(EMPTY_FORM)
  const [errors, setErrors] = useState<CheckoutFieldErrors>({})
  const [installmentPlan, setInstallmentPlan] = useState<GqlBasketInstallmentPlan | null>(null)

  // Fetch the unified cart-level installment plan whenever the cart contents change, and default
  // the selected installment count to the max interest-free option.
  useEffect(() => {
    let cancelled = false
    const fetchPlan = items.length === 0
      ? Promise.resolve(null)
      : getBasketInstallmentPlan(items).catch((error) => {
          console.error('Failed to fetch basket installment plan', error)
          return null
        })

    fetchPlan.then((plan) => {
      if (cancelled) return
      setInstallmentPlan(plan)
      if (plan) {
        setFormData((prev) => ({
          ...prev,
          installments: String(plan.maxInstallmentsWithoutInterest),
        }))
      }
    })
    return () => { cancelled = true }
  }, [items])

  // Pre-fill the shipping fields from the user's profile once authenticated. Only fills empty
  // fields so it never clobbers what the user has already typed.
  useEffect(() => {
    if (!user) return
    let cancelled = false
    getMyProfile()
      .then((p) => {
        if (cancelled) return
        setFormData((prev) => ({
          ...prev,
          name: prev.name || p.name || "",
          email: prev.email || p.email || "",
          address: prev.address || p.addressLine || "",
          city: prev.city || p.city || "",
        }))
      })
      .catch((error) => console.error('Failed to fetch profile for checkout prefill', error))
    return () => { cancelled = true }
  }, [user])

  const updateField = (field: Exclude<keyof CheckoutFormData, "paymentMethod">, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }))
  }

  const updatePaymentMethod = (paymentMethod: CheckoutPaymentMethod) => {
    setFormData((prev) => ({ ...prev, paymentMethod }))
  }

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setCheckoutError(null)
    const validationErrors = validateCheckoutForm(formData)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) return

    setState("processing")
    try {
      const id = await submitCheckout(formData, totalPrice)
      setOrderId(id)

      if (user) {
        addOrder({
          items: items.map((i) => ({
            name: i.product.name,
            quantity: i.quantity,
            price: i.product.price,
          })),
          total: totalPrice,
        })
      }

      clearCart()
      setState("success")
    } catch (err) {
      const message = err instanceof Error ? err.message : "An unexpected error occurred."
      setCheckoutError(message)
      setState("form")
    }
  }

  return {
    state,
    orderId,
    formData,
    errors,
    checkoutError,
    items,
    totalItems,
    totalPrice,
    installmentPlan,
    updateField,
    updatePaymentMethod,
    handleSubmit,
  }
}
