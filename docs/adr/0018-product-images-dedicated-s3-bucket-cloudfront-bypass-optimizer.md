# ADR-0018: Serve Product Catalog Images from a Dedicated S3 Bucket via CloudFront, Bypassing the Next.js Image Optimizer

## Status
**Proposed** — July 2026

---

## Context

Product images in the SPA render through `next/image`, so every catalog image becomes a `/_next/image?url=…&w=…&q=…` request. In production ([ADR-0014](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md)) CloudFront routes those to the **image-optimization Lambda** (the `_next/image*` behavior in `spa-distribution.ts`).

Two problems for a catalog:

- **Compute per image on a high-fanout screen.** A catalog list renders many products × several breakpoints. Each distinct `(url, w, q)` triggers the optimizer Lambda on first request — cost and cold-start latency exactly on the screen that must be fast. The optimizer's value (on-the-fly AVIF/WebP + `srcset`) is wasted on images we already control.
- **Images coupled to the site build.** Today product `imageUrl` is a single bundled asset (`/images/duck-hero.jpg` in `/public`). Changing a product image requires rebuilding and redeploying the whole SPA.

The catalog needs images decoupled from the build and served as pure CDN content.

---

## Decision

Serve product catalog images **directly from CloudFront → a dedicated S3 bucket**, outside the Next.js image optimizer. Images are uploaded **already web-ready** (sized + WebP/AVIF); no optimization pipeline.

### 1. Dedicated `product-images` bucket

A new S3 bucket (OAC-only, `BlockPublicAccess.BLOCK_ALL`, no versioning) holds product images, decoupled from the OpenNext assets bucket. Uploading/replacing a product image is independent of the SPA build.

### 2. CloudFront `product-images/*` behavior

A new behavior routes `product-images/*` straight to the bucket (S3 origin via OAC) with `CACHING_OPTIMIZED` — no Lambda in the path. It sits alongside the existing `_next/image*` (optimizer) and `images/*` (static `/public`) behaviors; `product-images/*` is a distinct, more-specific match.

### 3. Same-origin path + `unoptimized` in the SPA

Product `imageUrl` is a **same-origin path** `/product-images/<key>`. The three product image components render `next/image` with **`unoptimized`**, so Next emits a raw `<img src="/product-images/…">` — the browser fetches it directly, CloudFront matches `product-images/*`, and serves from S3. No `images.remotePatterns`, no custom loader, no `/_next/image`.

**Correct** — product images bypass the optimizer:

```tsx
// features/products/components/product-card.tsx
<Image src={product.imageUrl} alt={product.name} fill sizes="…" unoptimized />
```

The optimizer stays for the app's own UI assets (`hero-section`, `duck-mentor` — bundled in `/public`), which are few and low-traffic.

### 4. Dev/prod parity via one path

The same `/product-images/<key>` resolves in both environments:

| Env | Serves `/product-images/<key>` from |
|---|---|
| Dev (`pnpm dev`, no S3/CloudFront) | Next `/public/product-images/` |
| Prod | CloudFront `product-images/*` → S3 bucket |

The bucket is **not** seeded by CDK. Product images are uploaded straight to it via the AWS CLI/console (`aws s3 cp … s3://<bucket>/…`), fully decoupled from the SPA build — the point of the dedicated bucket. Local dev serves the same `/product-images/*` paths from the SPA's `public/product-images/` folder (a placeholder image lives there for local rendering).

---

## Applies To

- `infra/constructs/spa-storage.ts` — new `productImagesBucket` (not seeded by CDK; populated via AWS CLI).
- `infra/constructs/spa-distribution.ts` — `product-images/*` behavior + S3 origin.
- `infra/stacks/spa-stack.ts` — wiring.
- `src/WebApps/Shopping.Web.SPA.React` — `unoptimized` on the 3 product image components; `public/product-images/` seed; `next.config.mjs` comment.
- `src/Services/Catalog/Catalog.DevelopmentDataSeeder/CatalogInitialData.cs` — `imageUrl` → `/product-images/…`.

---

## Consequences

### Positive

- **No Lambda per catalog image** — product images are pure CloudFront/S3 CDN hits; cheapest and fastest for the highest-fanout screen.
- **Images decoupled from the build** — upload/replace product images in S3 without rebuilding or redeploying the SPA.
- **Minimal SPA change** — one `unoptimized` prop per product image component; no loader, no remotePatterns.
- **Dev keeps working** — the same path resolves from `/public` locally.

### Negative / Costs

- **No on-the-fly resizing/format negotiation** for product images — they must be uploaded already sized and in a modern format. A too-large upload ships as-is.
- **A second bucket to operate** (though trivially — static content, OAC-only).
- **Manual upload flow** — there is no admin UI yet; product images are uploaded to the bucket by hand via the AWS CLI/console. Dev renders a placeholder from `public/product-images/`.

### Mitigation Strategies

- Keep a lightweight convention for uploads (e.g. WebP, ~800px longest side). A future upload pipeline (S3-trigger Lambda generating variants) can be added later without changing this decision — it would only replace "upload ready" with "upload original".
- Because CDK never writes to the bucket, hand-uploaded images are never touched by a stack deploy (no `BucketDeployment` to overwrite/prune them).

### Future Constraints

- New product-image consumers MUST use the `/product-images/…` path with `unoptimized` (or a plain `<img>`); routing product images back through `/_next/image` re-introduces the per-image Lambda cost.
- If automatic optimization becomes necessary (uncontrolled/large uploads), revisit this ADR to add an S3-trigger optimization Lambda — not to move product images back onto the Next optimizer.

---

## Related Documentation

- [ADR-0014: Deploy the SPA via OpenNext on a Hand-Rolled CDK Stack](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md)
- [ADR-0009: AppSync Resolver Selection — Direct-First, Lambda for Complex Logic](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)

## References

- Next.js `next/image` — `unoptimized`, image optimization
- Amazon CloudFront — cache behaviors, S3 origin with Origin Access Control (OAC)
