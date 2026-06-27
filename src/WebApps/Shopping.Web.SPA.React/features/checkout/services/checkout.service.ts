import { gql } from "@/shared/lib/graphql-client"
import { storage } from "@/shared/lib/storage"
import { STORAGE_KEYS } from "@/shared/constants/storage-keys"
import type { CartItem } from "@/features/cart/types/cart.types"
import type { CheckoutFormData, CheckoutFieldErrors } from "@/features/checkout/types/checkout.types"
import type { GqlStoreBasketResult, GqlCheckoutResult } from "@/graphql/types"

const STORE_BASKET_MUTATION = `
  mutation StoreBasket($input: ShoppingCartInput!) {
    storeBasket(input: $input) { userName }
  }
`

const CHECKOUT_MUTATION = `
  mutation CheckoutBasket($input: CheckoutInput!) {
    checkoutBasket(input: $input) { isSuccess }
  }
`

/** Return a stable guest userName persisted in localStorage. */
export function getGuestUserName(): string {
  const existing = storage.getRaw(STORAGE_KEYS.guestUsername)
  if (existing) return existing
  const generated = `guest-${crypto.randomUUID()}`
  storage.setRaw(STORAGE_KEYS.guestUsername, generated)
  return generated
}

/** Persist cart items to DynamoDB via the storeBasket mutation (applies discounts server-side). */
export async function persistCart(userName: string, items: CartItem[]): Promise<void> {
  await gql<{ storeBasket: GqlStoreBasketResult }>(STORE_BASKET_MUTATION, {
    input: {
      userName,
      items: items.map((i) => ({
        quantity: i.quantity,
        price: i.product.price,
        productId: i.product.id,
        productName: i.product.name,
        color: null,
      })),
    },
  })
}

/**
 * Persist the cart then submit it for checkout via GraphQL.
 * Returns a generated client-side order ID on success (the Ordering service
 * creates the real order asynchronously via EventBridge).
 */
export async function submitCheckout(
  formData: CheckoutFormData,
  items: CartItem[],
  totalPrice: number,
): Promise<string> {
  const userName = getGuestUserName()

  await persistCart(userName, items)

  const nameParts = formData.name.trim().split(" ")
  const firstName = nameParts[0] ?? "Guest"
  const lastName = nameParts.slice(1).join(" ") || "-"

  // Parse "MM/YY" → "MM/20YY"
  const [month = "01", year = "26"] = formData.cardExpiry.split("/")
  const expiration = `${month.padStart(2, "0")}/20${year.trim()}`

  const data = await gql<{ checkoutBasket: GqlCheckoutResult }>(CHECKOUT_MUTATION, {
    input: {
      userName,
      customerId: crypto.randomUUID(),
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
