import { createHash } from 'node:crypto'

// OrderId = GUID(first 32 hex chars of SHA-256("<ownerId>#<checkoutId>")) — ADR-0050 §3. The same
// formula the checkout saga's DeriveOrderId state computes on AWS; the local GraphQL backend uses
// this copy, pinned to the ADR's test vectors, so both derive the same order id.
export function checkoutOrderId(ownerId: string, checkoutId: string): string {
  const h = createHash('sha256').update(`${ownerId}#${checkoutId}`).digest('hex')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20, 32)}`
}
