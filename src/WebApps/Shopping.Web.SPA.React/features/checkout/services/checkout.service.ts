import { createOrderId } from "@/shared/lib/id"
import type { CheckoutFormData, CheckoutFieldErrors } from "@/features/checkout/types/checkout.types"

/** Delay (ms) used to simulate payment processing. */
const PAYMENT_DELAY_MS = 2500

/**
 * Simulate a payment request and resolve with a generated order id.
 * Stands in for a real payment gateway call.
 */
export async function simulatePayment(): Promise<string> {
  await new Promise((resolve) => setTimeout(resolve, PAYMENT_DELAY_MS))
  return createOrderId()
}

/** Validate the checkout form, returning a map of field errors (empty when valid). */
export function validateCheckoutForm(data: CheckoutFormData): CheckoutFieldErrors {
  const errors: CheckoutFieldErrors = {}
  if (!data.name.trim()) errors.name = "Name is required"
  if (!data.email.trim() || !data.email.includes("@")) errors.email = "Invalid email"
  if (!data.address.trim()) errors.address = "Address is required"
  if (!data.city.trim()) errors.city = "City is required"
  if (!data.cardNumber.trim() || data.cardNumber.replace(/\s/g, "").length < 16) {
    errors.cardNumber = "Invalid card number"
  }
  if (!data.cardExpiry.trim()) errors.cardExpiry = "Expiry is required"
  if (!data.cardCvc.trim() || data.cardCvc.length < 3) errors.cardCvc = "Invalid CVC"
  return errors
}
