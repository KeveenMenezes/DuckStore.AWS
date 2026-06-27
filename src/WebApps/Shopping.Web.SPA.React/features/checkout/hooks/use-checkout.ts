"use client"

import { useState } from "react"
import type { FormEvent } from "react"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { submitCheckout, validateCheckoutForm } from "@/features/checkout/services/checkout.service"
import type {
  CheckoutFieldErrors,
  CheckoutFormData,
  CheckoutState,
} from "@/features/checkout/types/checkout.types"

const EMPTY_FORM: CheckoutFormData = {
  name: "",
  email: "",
  address: "",
  city: "",
  cardNumber: "",
  cardExpiry: "",
  cardCvc: "",
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

  const updateField = (field: keyof CheckoutFormData, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }))
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setCheckoutError(null)
    const validationErrors = validateCheckoutForm(formData)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) return

    setState("processing")
    try {
      const id = await submitCheckout(formData, items, totalPrice)
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
    updateField,
    handleSubmit,
  }
}
