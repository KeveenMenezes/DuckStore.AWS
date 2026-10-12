import { test } from 'node:test'
import assert from 'node:assert/strict'
import { checkoutOrderId } from './checkout-order-id.ts'

// ADR-0050 §3's vectors: the saga's DeriveOrderId state and this local copy must agree on them.
const vectors: Array<[ownerId: string, checkoutId: string, orderId: string]> = [
  ['USER#3f1c2a9e-0b7d-4c55-9a51-2e6f0d8b7c41', '7d0f5c3e-1a2b-4c3d-8e9f-0a1b2c3d4e5f', 'e9850bd4-ebf5-242c-bbc9-983db36d437a'],
  ['USER#3f1c2a9e-0b7d-4c55-9a51-2e6f0d8b7c41', '11111111-2222-4333-8444-555555555555', '380fc694-11be-8fed-9b71-c8cd6859df73'],
  ['USER#a0b1c2d3-e4f5-4a6b-8c7d-9e0f1a2b3c4d', '7d0f5c3e-1a2b-4c3d-8e9f-0a1b2c3d4e5f', '12fda24d-fa69-3cc3-a8f1-688b94844ce3'],
]

for (const [ownerId, checkoutId, orderId] of vectors) {
  test(`checkoutOrderId(${ownerId}, ${checkoutId})`, () => {
    assert.equal(checkoutOrderId(ownerId, checkoutId), orderId)
  })
}
