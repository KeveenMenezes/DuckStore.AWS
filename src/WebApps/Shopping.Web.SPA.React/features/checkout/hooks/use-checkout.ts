"use client"

import { useState } from "react"
import type { FormEvent } from "react"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { simulatePayment, validateCheckoutForm } from "@/features/checkout/services/checkout.service"
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
 * Owns the entire checkout flow: form state, validation, payment simulation,
 * order creation, and the form/processing/success state machine. The page
 * components stay presentational.
 */
export function useCheckout() {
  const { items, totalPrice, totalItems, clearCart } = useCart()
  const { user, addOrder } = useAuth()

  const [state, setState] = useState<CheckoutState>("form")
  const [orderId, setOrderId] = useState("")
  const [formData, setFormData] = useState<CheckoutFormData>(EMPTY_FORM)
  const [errors, setErrors] = useState<CheckoutFieldErrors>({})

  const updateField = (field: keyof CheckoutFormData, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }))
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    const validationErrors = validateCheckoutForm(formData)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) return

    setState("processing")
    const id = await simulatePayment()
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
  }

  return {
    state,
    orderId,
    formData,
    errors,
    items,
    totalItems,
    totalPrice,
    updateField,
    handleSubmit,
  }
}
