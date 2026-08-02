---
tags:
  - status/accepted
  - domain/user
---

# ADR-0017: User Bounded Context — Cognito as IdP-Only, Profile Data Owned by the User Service, Lazy Provisioning

## Status
**Accepted** — July 2026. §4 (resolver classification) **amended July 2026**: `myProfile` shipped
as a **direct** DynamoDB resolver, not the Lambda (`user-get-profile`) originally specified — see
the amendment note in §4. §2's `User.Function` service exists but is currently **dead code**: it
has no `[LambdaFunction]` methods and is not wired into `src/AppHost/UserExtensions.cs`; see
Consequences → Follow-up.

---

## Context

Authentication in the SPA is split between a real path and a fake one:

- The **Cognito Hosted UI PKCE flow already exists** end-to-end (`/api/auth/{login,callback,logout,me}` + `loginWithCognito()`), but no button is wired to it.
- The login/register buttons instead open a **local modal** (`auth-modal` → `login-form`/`register-form` → `auth.service`) that simulates accounts in `localStorage`. This is demo scaffolding, not authentication.

There is also **no owner of customer profile data**. Cognito holds only the essentials (email + password); there is no place for a display name, phone, or shipping address. The checkout form re-collects the address on every purchase, and `/my-profile` can only show the Cognito email/name.

We need: (1) real login/registration exclusively through the Hosted UI, deleting the simulated auth; and (2) a home for extended profile data that stays out of Cognito.

---

## Decision

Introduce a **`User` bounded context** that owns customer profile data, with **Cognito as an identity provider only**.

### 1. Ownership split

| Data | Owner |
|---|---|
| Credentials: email, password | **Cognito** (user pool `duckstore-users` — the customer/Shopping pool; staff sign in through a separate `duckstore-management-users` pool, out of scope for this ADR) |
| Profile: name, phone, addressLine, city, state, zipCode, country | **User service** (`user-profiles` table) |

Cognito holds only the sign-in credentials (email + password). Its schema is **left untouched** — Cognito does not allow modifying a live pool's standard attributes (an in-place add of a required `name` attribute is rejected with `Invalid AttributeDataType`), and standard attributes are fixed at pool creation. All profile data, **including the display name**, lives in the User service. `name` is seeded from the token's `email` claim on first access and is editable via `updateProfile`. Cognito MUST NOT be extended with custom profile attributes.

### 2. New service `src/Services/User/User.Function`

Mirrors `Catalog.Function` (Amazon.Lambda.Annotations + DynamoDB + MediatR). Table `user-profiles`, **partition key `UserId` = Cognito `sub` (raw, no prefix)** — the profile is always authenticated, exactly like Ordering's `CustomerId` ([ADR-0010](./0010-collapse-ordering-into-single-function-single-item-model.md)), and unlike the basket's prefixed `OwnerId` ([ADR-0016](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md)) which must distinguish guests.

### 3. Lazy provisioning (no event pipeline)

The profile record is created **on first authenticated read** (`myProfile`), seeded from the token claims — NOT via a Cognito Post-Confirmation → EventBridge pipeline. The Post-Confirmation trigger keeps its single responsibility (add to the `Customer` group).

> **Amendment (July 2026):** the diagram and resolver classification below describe the
> *original* design, where the get-or-create shape was judged complex enough to need a Lambda.
> As implemented, that shape collapsed into a single conditional `UpdateItem` — see the
> amendment note after §4's table for the current, shipped design.

```mermaid
sequenceDiagram
    participant B as Browser
    participant AS as AppSync
    participant L as user-get-profile (Lambda)
    participant D as DynamoDB (user-profiles)

    B->>AS: query myProfile   [Cognito Bearer]
    AS->>L: Invoke { UserId: sub, Email, Name }  (claims from ctx.identity)
    L->>D: GetItem(UserId)
    alt profile missing
        L->>D: PutItem (seeded from claims)
    end
    L-->>AS: UserProfile
    AS-->>B: UserProfile
```

Why lazy over event-driven: no eventual-consistency window (the profile exists the moment it's first needed), no extra SQS/DLQ consumer to operate, and the trigger stays decoupled from the User service's schema. The trade-off — the record doesn't exist until first login/profile access — is irrelevant because nothing reads a profile before the user is authenticated.

### 4. Resolver classification (per [ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md))

Both fields are **Cognito-only** (no `@aws_api_key`). Identity is always derived from `ctx.identity.sub` — the client never supplies a user id.

| Field | Resolver (as proposed) | Resolver (as shipped) | Justification |
|---|---|---|---|
| `myProfile: UserProfile!` | Lambda (`user-get-profile`) | **Direct** DynamoDB `UpdateItem` | Originally scoped as Criterion 1 (read then conditional create — not a single DynamoDB operation). In practice the get-or-create is expressible as one conditional `UpdateItem`; no Lambda needed. |
| `updateProfile(input): UserProfile!` | Direct DynamoDB `UpdateItem` | **Direct** DynamoDB `UpdateItem` (unchanged) | Single upsert keyed by `ctx.identity.sub`, `ReturnValues: ALL_NEW`. `email` is Cognito-owned and excluded from the input. |

**Amendment — `myProfile` shipped as a direct resolver, not a Lambda.** The get-or-create
semantics ("seed `Email`/`Name` from claims once, never overwrite on later calls") are
replicated with `if_not_exists` on a conditional `UpdateItem`, so the whole flow is a single
DynamoDB operation after all — the Criterion 1 justification for escalating to Lambda no longer
applies, and ADR-0009's direct-first default governs instead. There is no `user-get-profile`
Lambda; `src/Services/User/User.Function` has zero `[LambdaFunction]` methods and is not
registered in `src/AppHost/UserExtensions.cs`.

```mermaid
sequenceDiagram
    participant B as Browser
    participant AS as AppSync
    participant DS as UserProfilesDS (direct)
    participant D as DynamoDB (user-profiles)

    B->>AS: query myProfile   [Cognito Bearer]
    AS->>DS: UpdateItem(UserId=sub, if_not_exists(Email/Name, claims))
    DS->>D: UpdateItem
    D-->>DS: item (created or unchanged-and-returned)
    DS-->>AS: UserProfile
    AS-->>B: UserProfile
```

**Correct** — the direct resolver derives identity from `ctx.identity`, never a client-supplied id, and uses `if_not_exists` so the seed only ever applies once:

```js
// graphql/resolvers/user/queries/Query.myProfile.js
export function request(ctx) {
  if (!ctx.identity || !ctx.identity.sub) util.unauthorized()

  return {
    operation: 'UpdateItem',
    key: { UserId: util.dynamodb.toDynamoDB(ctx.identity.sub) },
    update: {
      expression: 'SET Email = if_not_exists(Email, :email), #name = if_not_exists(#name, :name)',
      expressionNames: { '#name': 'Name' },
      expressionValues: util.dynamodb.toMapValues({
        ':email': ctx.identity.claims.email,
        ':name': ctx.identity.claims.name,
      }),
    },
  }
}
```

### 5. Simulated auth is removed

The local `auth-modal`/`login-form`/`register-form`/`auth.service`/`auth-validation` are deleted. Login/registration go exclusively through the Hosted UI (`/api/auth/login`, with a `screen=signup` variant targeting the Cognito sign-up page). `AuthProvider` keeps only the Cognito path (`/api/auth/me`, `loginWithCognito`, `logout`).

---

## Applies To

- `src/Services/User/User.Function` (dead code as of the §4 amendment — see Follow-up) + `src/Services/User/User.DevelopmentDataSeeder` (table seeding only).
- `src/AppHost` — `UserExtensions.cs`, `Program.cs`.
- `infra` — `constructs/user-dynamodb.ts`, `stacks/user-stack.ts`, `bin/app.ts`, `constructs/appsync-api.ts` (`UserProfilesDS` + `myProfile`/`updateProfile` resolvers), `constructs/appsync-auth.ts` (add `name` attribute). No `user-lambdas.ts` exists — there is no Lambda in this bounded context.
- `src/WebApps/Shopping.Web.SPA.React` — delete simulated auth; wire Hosted UI login/signup; add `myProfile`/`updateProfile`; pre-fill checkout.

---

## Consequences

### Positive

- **Real authentication.** One code path (Cognito Hosted UI); the misleading `localStorage` simulation is gone.
- **Clear ownership.** Cognito = credentials; User service = profile. No profile data smeared into Cognito custom attributes.
- **Reusable profile.** Checkout pre-fills name/address; future features (saved addresses, preferences) have a home.
- **Simplest robust provisioning.** Lazy creation needs no event bus, no consumer, no idempotency ledger, and has no consistency gap.

### Negative / Costs

- **A new service to operate** — another `.Function`, seeder, table, stack, and AppSync data sources (though, per the §4 amendment, `User.Function` currently runs no Lambdas at all — see Follow-up below).
- **Local dev parity is limited** — profile is Cognito-only, so locally (no real Cognito) it falls back to the BFF-resolved owner, same limitation as local checkout ([ADR-0016](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md)).

~~**`myProfile` is a Lambda (cold start on first call).**~~ Resolved by the §4 amendment — `myProfile` is a direct resolver, no cold start.

### Mitigation Strategies

- `email`/`name` stay Cognito-authoritative: `updateProfile` never edits `email`; `myProfile` reseeds only when the attribute is absent (`if_not_exists`).

### Follow-up (identified July 2026)

`src/Services/User/User.Function` predates the §4 amendment and is now dead scaffolding — it
has no `[LambdaFunction]` methods, is not wired into `src/AppHost/UserExtensions.cs`, and nothing
in the codebase calls it. Specifically unreferenced:

- `Modules/Users/Domain/Entities/UserProfile.cs`
- `Modules/Users/Data/DynamoUserProfileRepository.cs` + `IUserProfileRepository.cs`
- `Modules/Users/Domain/Dtos/UserProfileDto.cs`

These should be deleted in a follow-up cleanup PR; `User.DevelopmentDataSeeder` (which only
creates the `user-profiles` table) is still live and should be kept.

### Future Constraints

- If profile creation ever needs to happen **before** first login (e.g. welcome email at signup), revisit this ADR to add the Post-Confirmation → EventBridge → User consumer path (with SQS/DLQ per [ADR-0015](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md)).
- New profile fields extend the `user-profiles` item and the `UpdateProfileInput`; the PK stays `UserId` = `sub`.

---

## Related Documentation

- [ADR-0016: Guest Shopping Carts — Unified ownerId Identity, API_KEY, and TTL](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0007: AppSync GraphQL — Direct DynamoDB Resolvers, Lambda for Business Logic](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md)
- [ADR-0010: Collapse Ordering into a Single Function, Single-Item Model](./0010-collapse-ordering-into-single-function-single-item-model.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)

## References

- Amazon Cognito — Hosted UI, user pool standard attributes, OAuth `profile` scope
- AWS AppSync — `ctx.identity` for Cognito User Pools authorization
