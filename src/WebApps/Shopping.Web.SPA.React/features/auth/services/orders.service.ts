import { gql } from "@/api"
import { GET_ORDERS_BY_CUSTOMER } from "@/api/queries/orders"
import type { Order, OrderStatus, PaymentMethodLabel } from "@/features/auth/types/auth.types"
import type { GqlOrderPage } from "@/graphql/types"

function mapBackendStatus(s: string): OrderStatus {
  if (s === "2" || /ship/i.test(s)) return "shipped"
  if (s === "3" || /deliv/i.test(s)) return "delivered"
  return "processing"
}

// Mirrors Ordering.Function's PaymentMethod enum (Debit=1, Credit=2, Cash=3).
const PAYMENT_METHOD_LABEL: Record<number, PaymentMethodLabel> = {
  1: "Debit Card",
  2: "Credit Card",
  3: "Cash",
}

export async function getOrdersForCustomer(customerId: string): Promise<Order[]> {
  const data = await gql<{ ordersByCustomer: GqlOrderPage }>(GET_ORDERS_BY_CUSTOMER, { customerId })
  return (data.ordersByCustomer.items ?? []).map((o) => ({
    id: o.id,
    date: o.createdAt ?? new Date().toISOString(),
    status: mapBackendStatus(o.status),
    total: o.orderItems.reduce((sum, i) => sum + i.price * i.quantity, 0),
    items: o.orderItems.map((i) => ({
      name: `Product ${i.productId.slice(0, 8)}`,
      quantity: i.quantity,
      price: i.price,
    })),
    shippingAddress: {
      firstName: o.shippingAddress.firstName,
      lastName: o.shippingAddress.lastName,
      emailAddress: o.shippingAddress.emailAddress,
      addressLine: o.shippingAddress.addressLine,
      country: o.shippingAddress.country,
      state: o.shippingAddress.state,
      zipCode: o.shippingAddress.zipCode,
    },
    payment: {
      method: PAYMENT_METHOD_LABEL[o.payment.paymentMethod] ?? "Credit Card",
      installments: o.payment.installments,
    },
  }))
}
