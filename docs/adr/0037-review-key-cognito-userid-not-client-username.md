---
tags:
  - status/accepted
  - domain/review
---

# ADR-0037: Review Composite Key — Cognito `sub` Instead of Client-Supplied `userName`

## Status
**Accepted** — July 2026

Supersedes [ADR-0029](./0029-review-upsert-composite-key-and-rating-delta.md) **on key composition
only** (§1, "Composite key replaces the random GUID"). ADR-0029's upsert mechanics, sort-order
stability (§2), pipeline-resolver shape (§3), rule-based Streams publisher (§4), and CatalogView
rating-delta aggregation (§5) are unaffected and remain in effect.

---

## Context

ADR-0029 made `reviews.Id` a deterministic composite key, `${productId}#${base64(userName)}`, so a
`PutItem` naturally upserts "the customer's review" instead of inserting an unbounded duplicate.
That decision took `userName` as given, carrying forward its original definition from
[ADR-0011](./0011-review-bounded-context-rating-aggregation-via-cdc.md) — an arbitrary,
client-supplied display string on `CreateReviewInput`.

That is the problem: **`userName` is the identity half of the key, but it is never verified.**
`Mutation.createReview.checkExisting.js`/`upsert.js` compute the id straight from
`ctx.args.input.userName` — a value the calling client fully controls. Any authenticated user can
pass any string as `userName`, including another customer's actual display name, and the resulting
`PutItem` overwrites that customer's existing review. Nothing in the schema, the resolver, or the
table prevents it; the composite-key upsert ADR-0029 designed to stop duplicate reviews is, by the
same mechanism, a way to silently clobber someone else's review.

This is inconsistent with every other identity-sensitive resolver in the codebase.
`Query.basket.js`, `Mutation.checkoutBasket.js`, `Mutation.mergeBasket.js`,
`Query.ordersByCustomer.js`, `Query.myProfile.js`, and `Mutation.updateProfile.js` all derive the
caller's identity from `ctx.identity.sub` — the Cognito-signed JWT subject — and never trust a
client-supplied identity field for a DynamoDB key ([ADR-0017](./0017-user-bounded-context-cognito-idp-only-lazy-provisioning.md)
§4 states this explicitly for `myProfile`/`updateProfile`). `createReview` was the one exception.

`userName` also serves a legitimate second purpose — the reviewer's display name shown on
`review-card.tsx` — which the fix below must not break.

---

## Decision

### 1. The composite key's identity component becomes `ctx.identity.sub`

```
Id = "${productId}#${userId}"     // userId := ctx.identity.sub
```

No encoding is applied. `ProductId` is a GUID; `userId` (a Cognito `sub`) is also always a UUID —
neither can contain the `#` separator, so ADR-0029's base64 step is no longer needed and is
dropped.

**Correct** — `checkExisting.js` derives the id from the verified identity, not `ctx.args.input`:

```js
export function request(ctx) {
  if (!ctx.identity?.sub) util.unauthorized()

  const { productId } = ctx.args.input
  const id = `${productId}#${ctx.identity.sub}`
  ctx.stash.id = id
  return { operation: 'GetItem', key: { Id: util.dynamodb.toDynamoDB(id) } }
}
```

**Incorrect** (the prior, superseded shape) — id derived from client input:

```js
export function request(ctx) {
  const { productId, userName } = ctx.args.input
  const id = `${productId}#${util.base64Encode(userName)}`   // client controls both halves
  ...
}
```

### 2. `UserName` becomes a display-only, non-key attribute sourced from the ID token

The item still carries a `UserName` attribute for `review-card.tsx` to render, but it is now
written from `ctx.identity.claims.name` at `PutItem` time — the same claim
`Query.myProfile.js`/`Mutation.updateProfile.js` already trust — never from client input. A new
`UserId` attribute holds the raw `sub` (the key's source value, kept as its own attribute for
clarity/debuggability, mirroring `user-profiles.UserId`).

```js
// Mutation.createReview.upsert.js
attributeValues: {
  ProductId: util.dynamodb.toDynamoDB(productId),
  UserId: util.dynamodb.toDynamoDB(ctx.identity.sub),
  UserName: util.dynamodb.toDynamoDB(ctx.identity.claims.name),
  ...
}
```

`CreateReviewInput.userName` is removed from the GraphQL schema — the client no longer sends it.
`CreateReviewResult` is widened to `{ id, userName }` so the client displays the server's
authoritative name immediately after submit, instead of assuming its local `user.name` matches
what got persisted.

### 3. Local dev backend

AppSync JS resolvers don't run locally (`app/api/graphql/local.ts` reimplements the same logic in
TypeScript against DynamoDB Local). There is no real Cognito session to read `claims.name` from
locally — the same limitation ADR-0017 already accepted for `myProfile`/checkout — so `userId`
comes from the BFF-resolved `context.owner.ownerId` (stripped of its `USER#`/`GUEST#` prefix) and
`userName` is seeded as that same `userId` placeholder, not a real display name.

### 4. Resolver classification is unchanged

`createReview` stays a **Direct** two-function pipeline resolver per
[ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) — reading
`ctx.identity.sub`/`ctx.identity.claims` is not an external call or business-rule branch, so
ADR-0029 §6's narrowing of criterion 1 still applies unchanged.

---

## Applies To

- `graphql/schema.graphql` — `CreateReviewInput` (drop `userName`), `CreateReviewResult` (add `userName`).
- `graphql/resolvers/reviews/mutations/Mutation.createReview.checkExisting.js`, `Mutation.createReview.upsert.js`.
- `src/Services/Review/Review.Function/Modules/Reviews/Data/ReviewSchema.cs` (`ComposeId`).
- `src/Services/Review/Review.Function/Modules/Reviews/EventsIntegration/Publishers/ReviewStreamImage.cs` (adds `UserId`).
- `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts`, `graphql/types.ts`, `api/mutations/review.ts`, `features/reviews/services/reviews.service.ts`, `features/reviews/components/review-form.tsx`.
- `tests/Services/Review/Review.UnitTests` — `ReviewStreamImageTests`, `ReviewCreatedRuleTests`, `ReviewUpdatedRuleTests`, new `ReviewSchemaTests`.

---

## Consequences

### Positive

- **Closes a real authorization gap.** A customer can no longer overwrite another customer's review by supplying their display name — the key's identity half is now cryptographically verified (Cognito-signed JWT), not client-asserted.
- **Consistent with the rest of the codebase.** `createReview` now follows the same `ctx.identity.sub`/`ctx.identity.claims` pattern already established by basket, orders, and profile resolvers — no bespoke identity handling left in Review.
- **Simpler key.** Dropping base64 encoding removes a step that existed only to guard against a collision risk (`userName` containing `#`) that a Cognito `sub` never has.
- **More trustworthy display name.** `UserName` reflects the Cognito `name` claim at write time rather than arbitrary client text, and the client no longer has to guess whether its local `user.name` matches what was persisted.

### Negative / Costs

- **Breaking schema change.** `CreateReviewInput.userName` removal and `CreateReviewResult`'s new `userName` field are both breaking for any external consumer of this mutation not tracked in this repo (low risk — the only known consumer is this SPA, updated in the same change).
- **Local dev parity gap widens slightly.** Locally, `UserName` is now a raw `userId` placeholder instead of an arbitrary human-readable string, since there is no real Cognito `name` claim to read outside a deployed environment — same class of limitation ADR-0017 already accepted for `myProfile`.

### Mitigation Strategies

- Keep `ReviewSchemaTests.ComposeId_ReturnsProductIdHashUserId_NoEncoding` and the updated `ReviewStreamImageTests`/`ReviewCreatedRuleTests`/`ReviewUpdatedRuleTests` fixtures in sync with the new shape so a future change can't silently reintroduce a client-controlled key component.
- Spot-check the deployed AppSync pipeline resolver against a real Cognito session before relying on it in production (no local emulation of `ctx.identity`, per ADR-0029 §"Negative / Costs").

### Future Constraints

- Any future AppSync resolver that composes a DynamoDB key or performs an authorization check from a client-supplied identity-shaped field (a username, an email, a customer id) instead of `ctx.identity.sub` should be treated as the same class of bug this ADR fixes, not a case-by-case judgment call.
- If a customer's Cognito `name` claim changes after a review was written, the stored `UserName` does not retroactively update — it reflects the name at the time of the write, not necessarily today's live claim. This is acceptable for now; revisit if display-name freshness becomes a requirement.

---

## Related Documentation

- [ADR-0029: Review Upsert — Composite Key and Rating-Delta Aggregation](./0029-review-upsert-composite-key-and-rating-delta.md)
- [ADR-0017: User Bounded Context — Cognito as IdP-Only, Profile Data Owned by the User Service, Lazy Provisioning](./0017-user-bounded-context-cognito-idp-only-lazy-provisioning.md)
- [ADR-0011: Review Bounded Context — Product Ratings Aggregated into Catalog via CDC](./0011-review-bounded-context-rating-aggregation-via-cdc.md)
- [ADR-0009: AppSync Resolver Selection — Direct DynamoDB Resolvers as Default](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)

## References

- Amazon Cognito — ID token `sub`/`name` claims, AppSync `ctx.identity` for Cognito User Pools authorization
