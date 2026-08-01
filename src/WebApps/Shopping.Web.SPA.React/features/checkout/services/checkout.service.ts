import { gql, gqlPublic } from "@/api"
import { CHECKOUT_BASKET } from "@/api/mutations/order"
import { GET_BASKET_INSTALLMENT_PLAN } from "@/api/queries/pricing"
import type { CheckoutFormData, CheckoutFieldErrors } from "@/features/checkout/types/checkout.types"
import type { CartItem } from "@/features/cart/types/cart.types"
import type { GqlCheckoutResult, GqlBasketInstallmentPlan } from "@/graphql/types"

// Mirrors Ordering.Function's PaymentMethod enum (Debit=1, Credit=2, Cash=3). The UI only ever
// offers a Card/Cash choice — Card always maps to Credit, matching the previous card-only flow.
const PAYMENT_METHOD_CODE: Record<CheckoutFormData["paymentMethod"], number> = {
  card: 2,
  cash: 3,
}

/**
 * Fetch the unified, cart-level installment plan (sums cost/originalPrice across every item,
 * then runs the whole cart through InstallmentCalculator as a single checkout transaction).
 * Public/cookie-free by default — same trust model as installmentPlanFor. Passing a discountId
 * switches to the authenticated client: applying a customer discount is Cognito-only (ADR-0046
 * §5), and the resolver rejects a discountId sent without ctx.identity.
 */
export async function getBasketInstallmentPlan(
  items: CartItem[],
  discountId?: string,
): Promise<GqlBasketInstallmentPlan | null> {
  if (items.length === 0) return null
  const client = discountId ? gql : gqlPublic
  const data = await client<{ basketInstallmentPlan: GqlBasketInstallmentPlan | null }>(
    GET_BASKET_INSTALLMENT_PLAN,
    {
      items: items.map((i) => ({ productId: i.product.id, quantity: i.quantity })),
      discountId: discountId ?? null,
    },
  )
  return data.basketInstallmentPlan
}

/**
 * Submit the cart for checkout via GraphQL. Checkout is Cognito-only: the AppSync resolver
 * derives OwnerId (USER#<sub>) and CustomerId from the token, so the browser sends neither.
 * The cart is already persisted in DynamoDB by the CartProvider sync — no extra storeBasket call.
 * Returns a generated client-side order ID (the Ordering service creates the real order asynchronously).
 */
export async function submitCheckout(
  formData: CheckoutFormData,
  totalPrice: number,
  discountId?: string,
): Promise<string> {
  const nameParts = formData.name.trim().split(" ")
  const firstName = nameParts[0] ?? "Guest"
  const lastName = nameParts.slice(1).join(" ") || "-"

  const isCash = formData.paymentMethod === "cash"

  // Parse "MM/YY" → "MM/20YY"
  const [month = "01", year = "26"] = formData.cardExpiry.split("/")
  const expiration = `${month.padStart(2, "0")}/20${year.trim()}`

  const data = await gql<{ checkoutBasket: GqlCheckoutResult }>(CHECKOUT_BASKET, {
    input: {
      totalPrice,
      // Opaque pass-through, chosen beforehand via basketInstallmentPlan(discountId:) — never
      // interpreted here (ADR-0046 §6).
      discountId: discountId ?? null,
      firstName,
      lastName,
      emailAddress: formData.email,
      addressLine: formData.address,
      country: "US",
      state: formData.city,
      zipCode: "00000",
      // Cash carries no card at all — the payment confirmation (QR code) is a separate,
      // later step. Only the name/email already collected above link the order to the customer.
      cardName: isCash ? null : formData.name,
      cardNumber: isCash ? null : formData.cardNumber.replace(/\s/g, ""),
      expiration: isCash ? null : expiration,
      cvv: isCash ? null : formData.cardCvc,
      paymentMethod: PAYMENT_METHOD_CODE[formData.paymentMethod],
      installments: isCash ? 1 : Number(formData.installments) || 1,
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
  // Address is optional (users can complete it later in /my-profile).
  if (!data.city.trim()) errors.city = "City is required"
  // Cash needs no card at all — payment confirmation is a separate, later step.
  if (data.paymentMethod === "card") {
    if (!data.cardNumber.trim() || data.cardNumber.replace(/\s/g, "").length < 16) {
      errors.cardNumber = "Invalid card number"
    }
    if (!data.cardExpiry.trim()) errors.cardExpiry = "Expiry is required"
    if (!data.cardCvc.trim() || data.cardCvc.length < 3) errors.cardCvc = "Invalid CVC"
  }
  return errors
}
