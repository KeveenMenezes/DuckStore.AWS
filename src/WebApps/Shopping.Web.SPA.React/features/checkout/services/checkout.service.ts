import { gql } from "@/api"
import { CHECKOUT_BASKET } from "@/api/mutations/order"
import { getGuestUserName, getGuestCustomerId } from "@/features/cart/services/basket.service"
import type { CheckoutFormData, CheckoutFieldErrors } from "@/features/checkout/types/checkout.types"
import type { GqlCheckoutResult } from "@/graphql/types"

export { getGuestUserName, getGuestCustomerId }

/**
 * Submit the cart for checkout via GraphQL.
 * The cart is already persisted in DynamoDB by the CartProvider sync — no extra storeBasket call needed.
 * Returns a generated client-side order ID (the Ordering service creates the real order asynchronously).
 */
export async function submitCheckout(
  formData: CheckoutFormData,
  totalPrice: number,
): Promise<string> {
  const userName = getGuestUserName()

  const nameParts = formData.name.trim().split(" ")
  const firstName = nameParts[0] ?? "Guest"
  const lastName = nameParts.slice(1).join(" ") || "-"

  // Parse "MM/YY" → "MM/20YY"
  const [month = "01", year = "26"] = formData.cardExpiry.split("/")
  const expiration = `${month.padStart(2, "0")}/20${year.trim()}`

  const data = await gql<{ checkoutBasket: GqlCheckoutResult }>(CHECKOUT_BASKET, {
    input: {
      userName,
      customerId: getGuestCustomerId(),
      totalPrice,
      firstName,
      lastName,
      emailAddress: formData.email,
      addressLine: formData.address,
      country: "US",
      state: formData.city,
      zipCode: "00000",
      cardName: formData.name,
      cardNumber: formData.cardNumber.replace(/\s/g, ""),
      expiration,
      cvv: formData.cardCvc,
      paymentMethod: 0,
    },
  })

  if (!data.checkoutBasket.isSuccess) {
    throw new Error("Checkout failed on the server. Please try again.")
  }

  return crypto.randomUUID()
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
