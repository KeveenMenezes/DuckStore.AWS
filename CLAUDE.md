# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

DuckStore is a **serverless-first AWS** .NET microservices e-commerce sample, orchestrated locally by .NET Aspire. It uses Vertical Slice Architecture, CQRS (MediatR), AWS Lambda functions, DynamoDB, and Amazon EventBridge. It's a learning/demo project, not production software — favor consistency with existing patterns over introducing new ones.

The codebase is mid-evolution: it was migrated from a classic stack (PostgreSQL/Marten, EF Core, RabbitMQ/MassTransit, gRPC, Carter) to serverless AWS. Architectural decisions are recorded under `docs/adr/` (40+ ADRs, numbered sequentially) — **read the relevant ADR before changing cross-cutting infrastructure**. Foundational ones: ADR-0004 (EventBridge over MassTransit/RabbitMQ), ADR-0005 (removal of in-process domain events; CDC via DynamoDB Streams), ADR-0006 (React/Next.js as the primary SPA), ADR-0019 (module-oriented service structure + rule-based stream publishers — the pattern every service below follows). Most recent: ADR-0040 (CatalogView inbound consumers grouped by producer bounded context via strategy dispatch). Use the `adr` skill to author/update ADRs.

Target framework is **net10.0** across all .NET projects.

## Common commands

```bash
# Build the whole solution
dotnet build DuckStore.sln

# Run everything (Aspire orchestrates Lambda functions, DynamoDB Local, the Lambda emulator,
# Elasticsearch/Kibana, and the web apps)
dotnet run --project src/AppHost/AppHost.csproj

# Run all .NET tests
dotnet test

# Run a single test project
dotnet test tests/Services/Catalog/Catalog.UnitTests/Catalog.UnitTests.csproj

# Run a single test by name
dotnet test --filter "FullyQualifiedName~CheckoutBasketCommandHandlerTests"

# Format code (also runs automatically on staged .cs files via .githooks/pre-commit)
dotnet format DuckStore.sln

# Validate infra/ (CDK) changes locally before pushing — CI runs `cdk deploy` per service on merge
cd infra && npx cdk synth <StackName>   # e.g. OrderingStack
cd infra && npx cdk diff <StackName>
```

Git hooks live in `.githooks/` (configured via `core.hooksPath`); the pre-commit hook runs `dotnet format` on staged `.cs` files and re-stages them. Don't bypass this with `--no-verify`.

Use `dotnet run --project src/AppHost/AppHost.csproj` (Aspire) to start the system locally — it provisions DynamoDB Local (`http://localhost:8000`), the Aspire AWS Lambda service emulator, and Elasticsearch/Kibana as containers, registers each Lambda function, and wires the dev environment between them. `docker-compose.yml`/`docker-compose.override.yml` are secondary/legacy to Aspire.

The Go notification service and the React SPA have their own toolchains (`go`, `pnpm`/`next`) — see those subsections.

## Architecture

### The serverless model

- **Compute** is AWS Lambda. Each use case is its own function. Locally, Aspire registers them via `AddAWSLambdaFunction<Projects.X>(...)` and runs them through the Lambda service emulator.
- **Persistence** is DynamoDB (DynamoDB Local in dev). Repositories talk to `IAmazonDynamoDB` directly (`TransactWriteItems`, etc.) — there is no EF Core `SaveChanges`/`ChangeTracker`/interceptor pipeline anymore.
- **Cross-service messaging** is Amazon EventBridge (`BuildingBlocks.Messaging/EventBridge`). The bus only exists on AWS; locally, publishes are **best-effort and fail silently**. Integration events are produced via **Change Data Capture**: a committed DynamoDB write → DynamoDB Streams → a Stream-triggered publisher Lambda → EventBridge (see ADR-0005). Do not reintroduce in-process domain events as the integration path. Lambda function names follow `<service>-<resource>-stream-publisher` for CDC-out publishers and `<service>-<resource>-sync-consumer`/`-consumer` for CDC-in consumers — keep new functions consistent with this.
- **Synchronous service-to-service calls** use direct Lambda invocation via the AWS Lambda Invoke API, not gRPC/HTTP (e.g. Payment → PaymentGateway). The former Basket → Discount invoke was removed by ADR-0012, which folded `Coupon` into Basket as an in-process entity; ADR-0026 later superseded that and moved discount/campaign ownership out of Basket entirely into Pricing — Basket now has zero discount responsibility and never calls Pricing, sync or async.

### Lambda function pattern (all services)

Each `*.Function` project uses the **Amazon.Lambda.Annotations** source generator:

- A `partial class Functions` holds methods decorated with `[LambdaFunction]` + `[HttpApi(...)]` (or other event-source attributes). The generator emits the actual handler types referenced in the AppHost as `X.Function::X.Function.Functions_<Method>_Generated::<Method>`. When you add/rename a function method, the AppHost `lambdaHandler` string must match the generated name.
- DI is configured in a `[LambdaStartup] public class Startup.ConfigureServices(IServiceCollection)`. Configuration comes from environment variables that Aspire injects (`ConnectionStrings__*`, `services__*`, `EventBridge__*`, AWS dev credentials/region).
- Endpoints map the HTTP request to a MediatR command/query via `ISender.Send(...)` and adapt request/response DTOs (Mapster `.Adapt<T>()`, convention-based).

### CQRS conventions (all .NET services)

- Commands implement `ICommand<TResponse>`, queries implement `IQuery<TResponse>` (from `BuildingBlocks.Core`), handled by `ICommandHandler<,>`/`IQueryHandler<,>` over MediatR.
- The MediatR pipeline in Lambda startups registers `ValidationBehavior` (FluentValidation `AbstractValidator<TCommand>`) and `LoggingBehavior` from `BuildingBlocks.ServiceDefaults.Behaviors`. (The old `UnitOfWorkBehavior` is gone — there is no DB transaction scope to manage.)
- Slices live under `Features/{UseCase}/` (e.g. `Features/Products/CreateProduct`, `Features/CheckoutBasket`), each holding endpoint + command/query + handler + validator together.

### Services (src/Services/*)

- **Basket.Function** — Lambda + DynamoDB (`IShoppingCartRepository`/`DynamoShoppingCartRepository`, no caching layer — ADR-0013; DI wired in `ServiceRegistration.AddBasketServices`). `ShoppingCart` is the aggregate root (`Aggregate<OwnerId>`); identity is a server-resolved, prefixed `OwnerId` — `USER#<cognito-sub>` for authenticated carts or `GUEST#<guestId>` for visitor carts, the latter TTL-expirable in DynamoDB (ADR-0016). Basket has **zero discount responsibility**: `Coupon`/`DynamoCouponRepository`/the `coupons` table were removed and discount/campaign ownership moved to Pricing (ADR-0026, supersedes ADR-0012 §1/§2). Checkout publishes to EventBridge (CDC). Slices under `Features/{StoreBasket,CheckoutBasket}`.
- **Catalog.Function** — Lambda + DynamoDB (`DynamoProductRepository`, `DynamoCategoryRepository`). Slices under `Features/Products/*` and `Features/Categories/*`.
- **Ordering.Function** — module-oriented like the rest of the reference architecture (`Modules/Orders/`), collapsed from the old `Ordering.{Domain,Application,Infrastructure}` DDD layering. Still keeps DDD types internally (`Order` aggregate + value objects, MediatR slices under `Features/`), but persistence is **DynamoDB** (`ordering`) via `DynamoOrderRepository`. Each order is a **single item** with its `OrderItems` embedded as a list attribute — written with one `PutItem` (atomic on its own; no `TransactWriteItems`), read by `Id` with `GetItem`, listed by customer through GSI1 (`ProjectionType.ALL`, no N+1). `Aggregate<TId>`/`IAggregate` are empty aggregate-root markers (domain events removed — ADR-0005). CDC Lambdas live under `Modules/Orders/EventsIntegration/`:
  - `Consumers/BasketCheckout/` — consumes the checkout integration event and writes the order (idempotent via the `ordering-processed-events` inbox).
  - `Consumers/PaymentResult/` — two `[LambdaFunction]` entry points (`OrderPaymentAuthorizedConsumer`/`OrderPaymentDeclinedConsumer`, one EventBridge rule each) both delegating to the same handler, which applies the Payment service's result to transition the order `Pending → Completed`/`Cancelled`.
  - `Publishers/OrderStreamPublisherFunction` — DynamoDB Streams source on `ordering`; publishes `OrderCreated` to EventBridge (CDC).
  - `Ordering.DevelopmentDataSeeder` — separate Worker that creates/seeds DynamoDB tables (`DynamoTableInitializer`, in the Function project) before the Ordering Lambdas start.
- **CatalogView.Function** — read-model/search bounded context fed by CDC from Catalog, Review, and Pricing (ADR-0027/ADR-0030, DynamoDB-backed). Inbound consumers are grouped **by producer bounded context**, not by individual event (ADR-0040): `Modules/Products/EventsIntegration/Consumers/CatalogSync/` (dispatcher + `ICatalogSyncStrategy` strategies for `ProductSynced`/`ProductDeleted`/`CatalogCategorySync`) and `Consumers/ReviewSync/` (dispatcher + `IReviewSyncStrategy` strategies for `ReviewCreated`/`ReviewUpdated`). `Consumers/PriceChanged/` stays a plain 1:1 handler since Pricing only produces one event here — grouping-of-one buys nothing. Each producer gets its own strategy interface; never share one across producers or fall back to an in-handler `switch`.
- **Payment.Function** — simulates payment authorization for orders (ADR-0025). Consumes `BasketCheckout` to create a `Payment`, applies a simulated gateway result via `PaymentGateway.Function` (Lambda invoke), and publishes `PaymentAuthorizedEvent`/`PaymentDeclinedEvent` via CDC. DynamoDB: `payments` + `payment-processed-events` (idempotency inbox).
- **PaymentGateway.Function** — the simulated external payment processor (`Modules/Gateway`), invoked synchronously by Payment; decides authorize/decline outcomes.
- **Pricing.Function** — owns product pricing and promotional campaigns (ADR-0026). `Campaigns` module (`CreateCampaign`/`EndCampaign` features), `Prices` module (consumes `ProductDeleted` for cleanup, publishes price changes via CDC, `GetInstallmentPlan`/`GetBasketInstallmentPlan` queries), `GatewayCosts` module (payment gateway cost tracking, ADR-0028). DynamoDB: `prices`, `campaigns`, `product-discounts`, `gateway-costs`, `pricing-processed-events`.
- **Review.Function** — product review/rating bounded context (ADR-0011/ADR-0029, upsert by composite key with rating delta). CDC-out only: `Modules/Reviews/EventsIntegration/Publishers/` publishes `ReviewCreated`/`ReviewUpdated` from the DynamoDB stream.
- **User.Function** — user profile bounded context backed by Cognito as the identity provider with lazy provisioning (ADR-0017); `DynamoUserProfileRepository` persists the profile projection.
- **ProductImages** — the only non-.NET service (`src/Services/ProductImages`, TypeScript/Node Lambdas, own `package.json`): `presign/` (presigned S3 POST for uploads), `processor/` (Sharp-based resize/optimize pipeline off an SQS queue), `scripts/sweep-orphan-images.ts` (cleanup). Dedicated S3/CloudFront bucket (ADR-0018/ADR-0034).
- **Notification** — Go service (`src/Services/Notification/Notification.Go`), consumes events and writes to DynamoDB. Has its own `go.mod` and `main_test.go` (`go test ./...`).

### BuildingBlocks (src/BuildingBlocks/*)

Shared code referenced across services — check here before adding cross-cutting concerns:

- **BuildingBlocks.Core** — `ICommand`/`IQuery`/handler interfaces, DDD base types (`Aggregate<TId>`/`IAggregate` as markers, `Entity<TId>`/`IEntity`, `ValueObject`), and shared exception types (`NotFoundException`, `BadRequestException`, `DomainException`, `InternalServerException`) surfaced as ProblemDetails by `CustomExceptionHandler`. Note: `IDomainEvent`/the domain-event machinery and `IUnitOfWork` were deleted (ADR-0005) — there's no unit-of-work or pagination abstraction; repositories query DynamoDB directly.
- **BuildingBlocks.Messaging** — `IntegrationEvent` base record and the EventBridge integration: `AddEventBridgeMessaging()` registers `IAmazonEventBridge` + `IEventPublisher` (`EventBridgePublisher`).
- **BuildingBlocks.ServiceDefaults** — `AddServiceDefaults()` (service discovery, Polly resilience, health checks, OpenTelemetry, Serilog → Elasticsearch), the shared MediatR `Behaviors`, and `CustomExceptionHandler` for ProblemDetails responses.
- **BuildingBlocks.ServiceDefaults.Lambda** — `AddLambdaDefaults()` (ADR-0022): the Lambda-shaped subset of ServiceDefaults — structured JSON logging to stdout/CloudWatch when running on AWS (plain console locally) and OpenTelemetry tracing of AWS SDK calls exported via OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Called by every `[LambdaStartup]` `Startup.ConfigureServices`. No Elasticsearch/health checks/service discovery (those assume a long-lived host).

### Orchestration and routing

- **src/AppHost** — .NET Aspire AppHost. `Program.cs` is the composition root; per-service wiring lives in one `*Extensions.cs` per service (`BasketExtensions`, `CatalogExtensions`, `CatalogViewExtensions`, `OrderingExtensions`, `PaymentExtensions`, `PricingExtensions`, `ReviewExtensions`, `UserExtensions`, plus `ObservabilityExtensions`). This is the source of truth for **local** infrastructure, Lambda handler names, `WaitFor`/`WaitForCompletion` chains, and DynamoDB Streams sources — for the real-AWS equivalent see `infra/` below. Shared helper in `Extensions/Extensions.cs`: `WithAwsDevEnvironment()` (dummy AWS creds + region for local dev). Add new resources/functions here, not in docker-compose.
- AppSync (direct DynamoDB resolvers, per ADR-0007/ADR-0009) is the only client entry point, in every environment — there is no self-hosted HTTP gateway. See ADR-0023. GraphQL schema/contract is owned at the monorepo root (ADR-0033): `graphql/schema.graphql` + resolver JS under `graphql/resolvers/<domain>/<queries|mutations>/<Type>.<field>.js`, wired into the API by `infra/constructs/appsync-api.ts` (`AppSyncStack`). Use the `resolver-selection` skill when deciding direct-DynamoDB vs. Lambda for a new field; a third kind now exists too — `createProductWithPrice` resolves through an HTTP data source into a Step Functions Express saga (`infra/constructs/product-create-saga.ts`, ADR-0032) that writes the product and its price with compensation, still invoked only from behind AppSync.
- **src/WebApps/Shopping.Web.SPA.React** — React/Next.js SPA, the customer-facing storefront, fronting AppSync through its own Next.js BFF. Uses `pnpm` (`pnpm dev` / `pnpm build` / `pnpm lint`). Deployed via SST/OpenNext, not the CDK below (ADR-0020). This directory is slated to move into its own Git repository, linked back into DuckStore as a **git submodule**.
- **src/WebApps/Managment.Web.Blazor** — Blazor WebAssembly admin app (product create/edit/list), calling AppSync GraphQL directly (no BFF). Deployed as a static site to S3/CloudFront. It's the client for the `createProductWithPrice` saga mutation above.

### Deployment (`infra/`)

`infra/` is a separate AWS CDK v2 TypeScript app (`infra/bin/app.ts`) — **real AWS deployment infrastructure, distinct from Aspire**. Aspire (above) only orchestrates local dev; it never touches real AWS resources. One CDK stack per service (`CatalogStack`, `BasketStack`, `OrderingStack`, `PaymentStack`, `PricingStack`, `ReviewStack`, `CatalogViewStack`, `UserStack`, plus `AppSyncStack`, `ManagementStack`, `ProductImagesStack`, `MonitoringStack`, `FoundationStack`), each pairing an `infra/constructs/<service>-dynamodb.ts` (tables) with an `infra/constructs/<service>-lambdas.ts` (functions, EventBridge rules, IAM grants, DLQs). `MonitoringStack` deploys first and owns the shared `duckstore-alerts` SNS topic every DLQ alarm imports; `FoundationStack` also deploys first and owns the shared `duckstore-event-bus` EventBridge bus every service publishes to and consumes from (`RemovalPolicy.RETAIN` — a resource read by 7 stacks must not block deletion, or live inside one of its own consumers). Every stack that needs a shared resource (the bus, the alerts topic, another stack's DynamoDB table, another stack's Lambda for an AppSync data source) references it **by fixed physical name** (`EventBus.fromEventBusName`, `Function.fromFunctionName`, `Table.fromTableName`), never `Fn.importValue`/CloudFormation exports — cross-stack `Fn.importValue` creates a hard CloudFormation dependency that blocks the exporting stack's own updates (and defeats `--exclusively`) as soon as anything imports the export. This is why every `*.Function` project's Lambdas keep a fixed `functionName` in their CDK definition — the stable name is the whole mechanism, not an incidental detail. CI/CD is per-service GitHub Actions workflows (`.github/workflows/deploy-<service>-cdk.yml`) using OIDC, triggered on push to `development` for changes under `infra/**`, `src/Services/<X>/**`, or `src/BuildingBlocks/**`; publishes `linux-arm64` Lambda artifacts and runs `cdk deploy --require-approval never`. Add new AWS resources for a service in its `infra/constructs/*.ts` pair (mirroring the equivalent Aspire wiring in `src/AppHost/*Extensions.cs`), not just in AppHost.

## Testing

- Unit tests: xUnit + Moq + Moq.AutoMock, one project per service under `tests/Services/<Service>/<Service>.UnitTests` (plus `tests/BuildingBlocks/BuildingBlocks.UnitTests`). Tests target handlers directly, mocking repository/dependency interfaces.
- Functional tests (`Ordering.FunctionalTests`): use `DistributedApplicationTestingBuilder` to spin up the real Aspire app graph (including containers) and exercise actual endpoints after waiting for resource health. Slower; require Docker.
- Packages/versions are centrally pinned in `Directory.Packages.props` (central package management) — add package references there, not inline versions in `.csproj` files.
