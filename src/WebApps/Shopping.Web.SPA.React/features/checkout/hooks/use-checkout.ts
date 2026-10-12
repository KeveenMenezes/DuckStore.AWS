"use client"

import { useEffect, useRef, useState } from "react"
import type { FormEvent } from "react"
import { GraphQLRequestError } from "@/api"
import { useCart } from "@/features/cart/hooks/use-cart"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { getMyProfile } from "@/features/auth/services/profile.service"
import { usePointsBalance } from "@/features/points/hooks/use-points-balance"
import { REDEMPTION_REFRESH_DELAY_MS } from "@/features/points/constants"
import { createCheckoutId } from "@/shared/lib/id"
import {
  chargedTotalFor,
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

// Typing a number of points re-quotes the cart; wait for the customer to stop typing.
const POINTS_QUOTE_DEBOUNCE_MS = 400

// The server's card field names (InvalidCard errorInfo.field) mapped to this form's fields.
const CARD_FIELDS: Record<string, keyof CheckoutFieldErrors> = {
  CardNumber: "cardNumber",
  Expiration: "cardExpiry",
  Cvv: "cardCvc",
}

/**
 * Owns the entire checkout flow: form state, validation, the points to use, the server quote,
 * the checkout mutation, and the form/processing/success state machine.
 *
 * The total shown and sent is always the quote's total for the chosen payment method
 * (chargedTotalFor) — the same figure Pricing recomputes and compares in the checkout pipeline,
 * which rejects any difference with PriceChanged (ADR-0048 §4).
 *
 * Every submit of one checkout attempt carries the same checkoutId, so a retry after a timeout or
 * a network error gets the order the first try created instead of a second one. The id is renewed
 * only after a success or a typed refusal — never after InternalError, which may hide a checkout
 * that did go through (ADR-0050 §3).
 */
export function useCheckout() {
  const { items, totalPrice, totalItems, clearCart } = useCart()
  const { user, addOrder, refreshOrders } = useAuth()
  const { balance, refresh: refreshBalance } = usePointsBalance()

  const [state, setState] = useState<CheckoutState>("form")
  const [orderId, setOrderId] = useState<string | null>(null)
  const [checkoutError, setCheckoutError] = useState<string | null>(null)
  const [formData, setFormData] = useState<CheckoutFormData>(EMPTY_FORM)
  const [errors, setErrors] = useState<CheckoutFieldErrors>({})
  const [installmentPlan, setInstallmentPlan] = useState<GqlBasketInstallmentPlan | null>(null)
  const [isLoadingInstallmentPlan, setIsLoadingInstallmentPlan] = useState(false)
  const [isLoadingProfile, setIsLoadingProfile] = useState(false)

  const [pointsToUse, setPointsToUse] = useState(0)
  const [quotedPoints, setQuotedPoints] = useState(0)
  const [pointsNotice, setPointsNotice] = useState<string | null>(null)
  // Set when the server refused the checkout with PriceChanged: the new total the customer must
  // look at (and confirm again) before checking out.
  const [priceChangedTotal, setPriceChangedTotal] = useState<number | null>(null)
  // Bumped to force a fresh quote of the same cart and points (after PriceChanged).
  const [quoteVersion, setQuoteVersion] = useState(0)
  const quotedItemsRef = useRef<typeof items | null>(null)
  const checkoutIdRef = useRef<string>(createCheckoutId())

  // Debounce the points before re-quoting.
  useEffect(() => {
    const timer = setTimeout(() => setQuotedPoints(pointsToUse), POINTS_QUOTE_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [pointsToUse])

  // Fetch the unified cart-level quote whenever the cart contents or the points change. A new cart
  // defaults the installment count to the max interest-free option; a points change keeps the
  // customer's choice when it is still offered.
  useEffect(() => {
    let cancelled = false
    if (items.length === 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setInstallmentPlan(null)
      setIsLoadingInstallmentPlan(false)
      return
    }

    const cartChanged = quotedItemsRef.current !== items
    quotedItemsRef.current = items

    setIsLoadingInstallmentPlan(true)
    getBasketInstallmentPlan(items, user ? quotedPoints : 0)
      .catch((error) => {
        // Above the cap (the cart got cheaper, or a stale field): use the cap and re-quote —
        // never silently charge with fewer points than the field shows (ADR-0048 §3).
        if (error instanceof GraphQLRequestError && error.errorType === "PointsAboveCap") {
          const max = Number(error.errorInfo?.maxRedeemablePoints ?? 0)
          if (!cancelled) {
            setPointsToUse(max)
            setPointsNotice(`Adjusted to ${max} points, the most this order allows.`)
          }
          return null
        }
        console.error('Failed to fetch basket installment plan', error)
        return null
      })
      .then((plan) => {
        if (cancelled) return
        setInstallmentPlan(plan)
        setIsLoadingInstallmentPlan(false)
        if (!plan) return
        setFormData((prev) => {
          const offered = Number(prev.installments) === 1
            || plan.installments.some((i) => i.count === Number(prev.installments))
          return cartChanged || !offered
            ? { ...prev, installments: String(plan.maxInstallmentsWithoutInterest) }
            : prev
        })
      })
    return () => { cancelled = true }
  }, [items, quotedPoints, user, quoteVersion])

  // Most points the customer can use on this order: the cart's cap, limited by the balance.
  const maxPoints = Math.max(0, Math.min(balance, installmentPlan?.maxRedeemablePoints ?? 0))

  // Keep the field within the maximum when either side drops (re-quote, balance refresh).
  useEffect(() => {
    if (!installmentPlan || pointsToUse <= maxPoints) return
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPointsToUse(maxPoints)
    setPointsNotice(`Adjusted to ${maxPoints} points, the most you can use on this order.`)
  }, [installmentPlan, maxPoints, pointsToUse])

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

  // The quote is only current when it was made for the points in the field.
  const isQuoteCurrent = !isLoadingInstallmentPlan && quotedPoints === pointsToUse
  const chargedTotal = isQuoteCurrent
    ? chargedTotalFor(installmentPlan, formData.paymentMethod, Number(formData.installments) || 1)
    : null

  const updateField = (field: Exclude<keyof CheckoutFormData, "paymentMethod">, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }))
  }

  const updatePaymentMethod = (paymentMethod: CheckoutPaymentMethod) => {
    setFormData((prev) => ({ ...prev, paymentMethod }))
  }

  const updatePointsToUse = (points: number) => {
    setPointsNotice(null)
    setPointsToUse(points)
  }

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setCheckoutError(null)
    const validationErrors = validateCheckoutForm(formData)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) return
    // The button is disabled without a current quote; this only guards a race with a re-quote.
    if (chargedTotal === null) return

    setState("processing")
    try {
      const id = await submitCheckout(formData, chargedTotal, pointsToUse, checkoutIdRef.current)
      checkoutIdRef.current = createCheckoutId()
      setOrderId(id)
      setPriceChangedTotal(null)

      if (user) {
        addOrder({
          id: id ?? undefined,
          items: items.map((i) => ({
            name: i.product.name,
            imageId: i.product.imageId,
            quantity: i.quantity,
            price: i.product.price,
          })),
          total: chargedTotal,
        })

        // The optimistic entry above has no shippingAddress/payment (this form doesn't collect
        // everything Order needs) — replace it with the real, fully-synced order once Ordering
        // has processed the checkout event. A short delay covers Basket -> EventBridge -> Ordering.
        setTimeout(() => { void refreshOrders() }, 2500)
        // The points are debited by Challenges off the same event, asynchronously.
        if (pointsToUse > 0) setTimeout(() => { void refreshBalance() }, REDEMPTION_REFRESH_DELAY_MS)
      }

      clearCart()
      setState("success")
    } catch (err) {
      setState("form")
      // A typed refusal means this attempt is over; anything else may have checked out, so the
      // next submit keeps the id and gets that order back if it exists.
      if (err instanceof GraphQLRequestError && err.errorType && err.errorType !== "InternalError") {
        checkoutIdRef.current = createCheckoutId()
      }
      if (err instanceof GraphQLRequestError && err.errorType === "PriceChanged") {
        // Never resubmit on the customer's behalf: show the new total and let them confirm it.
        const serverTotal = Number(err.errorInfo?.serverTotal)
        setPriceChangedTotal(Number.isFinite(serverTotal) ? serverTotal : null)
        setQuoteVersion((v) => v + 1)
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "PointsAboveCap") {
        const max = Number(err.errorInfo?.maxRedeemablePoints ?? 0)
        setPointsToUse(max)
        setPointsNotice(`Adjusted to ${max} points, the most this order allows. Review the total and confirm again.`)
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "CartChanged") {
        setCheckoutError("Your cart changed in another window. Review it and confirm again.")
        setQuoteVersion((v) => v + 1)
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "ProductUnavailable") {
        const unavailable = (err.errorInfo?.productIds as string[] | undefined) ?? []
        const names = items.filter((i) => unavailable.includes(i.product.id)).map((i) => i.product.name)
        setCheckoutError(
          `${names.length > 0 ? names.join(", ") : "Some products"} ${names.length === 1 ? "is" : "are"} no longer available. Remove ${names.length === 1 ? "it" : "them"} from your cart to continue.`,
        )
        setQuoteVersion((v) => v + 1)
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "InvalidCard") {
        const field = CARD_FIELDS[String(err.errorInfo?.field)] ?? "cardNumber"
        setErrors((prev) => ({ ...prev, [field]: err.message }))
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "CheckoutRetryRequired") {
        setCheckoutError("This checkout expired. Review your order and confirm again.")
        setQuoteVersion((v) => v + 1)
        return
      }
      if (err instanceof GraphQLRequestError && err.errorType === "InternalError") {
        setCheckoutError("We couldn't confirm your order. Try again — you won't be charged twice.")
        return
      }
      const message = err instanceof Error ? err.message : "An unexpected error occurred."
      setCheckoutError(message)
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
    chargedTotal,
    installmentPlan,
    isLoadingInstallmentPlan: isLoadingInstallmentPlan || quotedPoints !== pointsToUse,
    isLoadingProfile,
    isSignedIn: user !== null,
    balance,
    pointsToUse,
    maxPoints,
    pointsNotice,
    priceChangedTotal,
    updateField,
    updatePaymentMethod,
    updatePointsToUse,
    handleSubmit,
  }
}
