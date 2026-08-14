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
  const { user, addOrder, refreshOrders } = useAuth()

  const [state, setState] = useState<CheckoutState>("form")
  const [orderId, setOrderId] = useState("")
  const [checkoutError, setCheckoutError] = useState<string | null>(null)
  const [formData, setFormData] = useState<CheckoutFormData>(EMPTY_FORM)
  const [errors, setErrors] = useState<CheckoutFieldErrors>({})
  const [installmentPlan, setInstallmentPlan] = useState<GqlBasketInstallmentPlan | null>(null)
  const [isLoadingInstallmentPlan, setIsLoadingInstallmentPlan] = useState(false)
  const [isLoadingProfile, setIsLoadingProfile] = useState(false)

  // Fetch the unified cart-level installment plan whenever the cart contents change, and default
  // the selected installment count to the max interest-free option.
  useEffect(() => {
    let cancelled = false
    if (items.length === 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setInstallmentPlan(null)
      setIsLoadingInstallmentPlan(false)
      return
    }

    setIsLoadingInstallmentPlan(true)
    getBasketInstallmentPlan(items)
      .catch((error) => {
        console.error('Failed to fetch basket installment plan', error)
        return null
      })
      .then((plan) => {
        if (cancelled) return
        setInstallmentPlan(plan)
        setIsLoadingInstallmentPlan(false)
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
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsLoadingProfile(true)
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
      .finally(() => {
        if (!cancelled) setIsLoadingProfile(false)
      })
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
            imageId: i.product.imageId,
            quantity: i.quantity,
            price: i.product.price,
          })),
          total: totalPrice,
        })

        // The optimistic entry above has no shippingAddress/payment (this form doesn't collect
        // everything Order needs) — replace it with the real, fully-synced order once Ordering
        // has processed the checkout event. A short delay covers Basket -> EventBridge -> Ordering.
        setTimeout(() => { void refreshOrders() }, 2500)
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
    isLoadingInstallmentPlan,
    isLoadingProfile,
    updateField,
    updatePaymentMethod,
    handleSubmit,
  }
}
