# Architecture Decision Records

Index of all ADRs, per the governance defined in
[ADR-0000](./0000-official-architecture-decisios-records-standard.md). ADRs are never deleted,
even when `Superseded` or `Obsolete` — the table below is the fastest way to find the current
decision without opening every file.

| # | Title | Status |
|---|---|---|
| [0000](./0000-official-architecture-decisios-records-standard.md) | Official Architecture Decision Records (ADR) Standard | Accepted |
| [0001](./0001-elasticsearch-integration-guideline.md) | Elasticsearch Integration Guideline | Obsolete — pre-serverless; see [0022](./0022-lambda-production-observability.md) |
| [0002](./0002-elasticsearch-index-lifecycle-management.md) | Elasticsearch Index Lifecycle Management (ILM) | Obsolete — pre-serverless; see [0022](./0022-lambda-production-observability.md) |
| [0003](./0003-adoption-of-zero-trust-security-model.md) | Adoption of Zero Trust Security Model | Accepted |
| [0004](./0004-aws-first-eventbridge-over-masstransit-rabbitmq.md) | AWS-First Messaging — Replace MassTransit/RabbitMQ with Amazon EventBridge | Accepted |
| [0005](./0005-remove-domain-events-cdc-via-dynamodb-streams.md) | Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC) | Accepted |
| [0006](./0006-react-nextjs-spa-over-angular.md) | Adopt React/Next.js as the Primary SPA, Replacing Angular | Accepted |
| [0007](./0007-appsync-graphql-with-direct-dynamodb-resolvers.md) | AWS AppSync (GraphQL) — Direct DynamoDB Resolvers for Reads, Lambda for Business Logic | Accepted |
| [0008](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md) | Extend CDC Event Publishing — Basket ShoppingCarts Stream Publisher | Accepted |
| [0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) | AppSync Resolver Selection — Direct First, Lambda for Complex Logic | Accepted |
| [0010](./0010-collapse-ordering-into-single-function-single-item-model.md) | Collapse Ordering into a Single Function Project with a Single-Item DynamoDB Model | Accepted |
| [0011](./0011-review-bounded-context-rating-aggregation-via-cdc.md) | Review Bounded Context — Product Ratings Aggregated into Catalog via CDC | Accepted |
| [0012](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md) | Merge Discount into Basket — Coupon as an In-Process Entity | Superseded → [0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md) |
| [0013](./0013-remove-basket-caching-redis-and-dax.md) | Remove All Basket Caching — Redis Cache-Aside and DynamoDB DAX | Accepted |
| [0014](./0014-deploy-spa-via-opennext-hand-rolled-cdk.md) | Deploy the React SPA to AWS via OpenNext, Hand-Rolled CDK | Superseded → [0020](./0020-migrate-spa-deploy-to-sst.md) |
| [0015](./0015-sqs-dlq-for-cdc-publishers-and-eventbridge-consumers.md) | SQS Dead-Letter Queues for CDC Publishers and EventBridge Consumers | Accepted |
| [0016](./0016-guest-basket-owner-id-identity-api-key-and-ttl.md) | Guest Shopping Carts — Unified `ownerId` Identity, API_KEY, and TTL | Accepted (amended by [0041](./0041-bff-opaque-server-side-session-centralized-cognito-refresh.md)) |
| [0017](./0017-user-bounded-context-cognito-idp-only-lazy-provisioning.md) | User Bounded Context — Cognito as IdP-Only, Lazy Provisioning | Accepted |
| [0018](./0018-product-images-dedicated-s3-bucket-cloudfront-bypass-optimizer.md) | Product Catalog Images — Dedicated S3 Bucket via CloudFront | Superseded → [0034](./0034-product-image-pipeline-presigned-post-sqs-sharp-cloudfront.md) |
| [0019](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) | Module-Oriented Service Structure and Rule-Based Stream Publishers | Accepted |
| [0020](./0020-migrate-spa-deploy-to-sst.md) | Migrate the SPA Deploy Layer from Hand-Rolled CDK to SST | Accepted (supersedes 0014) |
| [0021](./0021-fail-fast-eventbridge-publishing-on-aws.md) | Fail-Fast EventBridge Publishing on AWS — Best-Effort Only Locally | Accepted |
| [0022](./0022-lambda-production-observability.md) | Production Observability for Lambda Functions | Accepted |
| [0023](./0023-decommission-yarp-gateway-and-angular-spa.md) | Decommission the YARP Gateway and Complete the Angular SPA Removal | Accepted |
| [0024](./0024-testing-strategy-minimum-coverage.md) | Testing Strategy and Minimum Coverage Standard | Accepted |
| [0025](./0025-payment-bounded-context-simulated-gateway-cdc.md) | Payment Bounded Context — Simulated Gateway via Async EventBridge/CDC | Accepted |
| [0026](./0026-pricing-bounded-context-price-and-campaign-ownership.md) | Pricing Bounded Context — Price and Campaign Ownership Move from Catalog/Basket | Accepted (supersedes 0012 §1/§2) |
| [0027](./0027-catalogview-opensearch-product-search-and-rating-sync.md) | CatalogView — Product Search and Rating Aggregation via OpenSearch | Superseded → [0030](./0030-catalogview-dynamodb-drop-opensearch.md) |
| [0028](./0028-gateway-cost-table-and-payment-highlights.md) | Gateway Cost Table and Payment Highlights | Accepted |
| [0029](./0029-review-upsert-composite-key-and-rating-delta.md) | Review Upsert — Composite Key and Rating-Delta Aggregation | Accepted |
| [0030](./0030-catalogview-dynamodb-drop-opensearch.md) | CatalogView Goes DynamoDB-Backed — Drop OpenSearch | Accepted (supersedes 0027) |
| [0031](./0031-cdc-events-named-after-domain-occurrence-no-changetype-discriminator.md) | CDC Events Named After the Domain Occurrence, Never a ChangeType Discriminator | Accepted |
| [0032](./0032-create-product-with-price-step-functions-express-saga.md) | Product Creation With Price as a Step Functions Express Saga | Accepted |
| [0033](./0033-graphql-contract-owned-at-monorepo-root.md) | GraphQL Contract and Resolvers Owned at the Monorepo Root | Accepted |
| [0034](./0034-product-image-pipeline-presigned-post-sqs-sharp-cloudfront.md) | Product Image Pipeline — Presigned POST, S3→SQS, CloudFront | Accepted (supersedes 0018) |
| [0035](./0035-catalogview-owned-cdc-events-drive-spa-revalidation.md) | CatalogView-Owned CDC Events Drive SPA Cache Revalidation | Accepted |
| [0036](./0036-federation-client-secrets-noecho-parameters-github-secrets.md) | Federation Client Secrets as NoEcho Parameters from GitHub Secrets | Accepted |
| [0037](./0037-review-key-cognito-userid-not-client-username.md) | Review Composite Key — Cognito `sub` Instead of Client-Supplied `userName` | Accepted |
| [0038](./0038-order-drops-card-data-payment-sources-basketcheckout.md) | Order Stops Persisting Card Data | Accepted |
| [0039](./0039-order-analytics-pipeline-eventbridge-firehose-s3.md) | Order Analytics Pipeline via EventBridge, Firehose, and S3 | Proposed — not yet implemented |
| [0040](./0040-catalogview-consumers-consolidated-by-producer-strategy-dispatch.md) | CatalogView Consumers Consolidated by Producer via Strategy Dispatch | Accepted |
| [0041](./0041-bff-opaque-server-side-session-centralized-cognito-refresh.md) | BFF-Owned Opaque Server-Side Sessions — Centralized Cognito Token Refresh | Accepted |
| [0042](./0042-lambda-native-aot-zip-provided-al2023.md) | .NET Lambdas Ship as Native AOT ZIPs on `provided.al2023` | Accepted |
| [0043](./0043-cart-discount-allocation-policy.md) | Cart Discount Allocation — Per-Product Campaigns Against a Single-Transaction Cart | Accepted |
| [0044](./0044-campaign-cdc-product-discounts-stream-and-ttl.md) | Campaign Changes Reach CatalogView via a `product-discounts` Stream and TTL | Accepted |
| [0045](./0045-challenges-bounded-context-server-side-grading.md) | Challenges Bounded Context — Server-Side Grading and Answer-Key Isolation | Accepted |
| [0046](./0046-challenge-points-redeem-into-pricing-customer-discount.md) | Challenge Points Redeem into a Pricing Customer Discount via CDC | Accepted |
| [0047](./0047-catalogview-gsi1-single-partition-key-kept-deliberately.md) | CatalogView's GSI1 Keeps a Single Partition Key Value — Deliberately | Proposed |

## Superseded chains

- 0012 → 0026 (discount/coupon ownership: Basket → Pricing)
- 0014 → 0020 (SPA deploy: hand-rolled CDK → SST)
- 0018 → 0034 (product images: bare S3/CloudFront → presigned-POST pipeline)
- 0027 → 0030 (CatalogView search: OpenSearch → DynamoDB)
