# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

DuckStore is a **serverless-first AWS** .NET microservices e-commerce sample, orchestrated locally by .NET Aspire. It uses Vertical Slice Architecture, CQRS (MediatR), AWS Lambda functions, DynamoDB, and Amazon EventBridge. It's a learning/demo project, not production software — favor consistency with existing patterns over introducing new ones.

The codebase is mid-evolution: it was migrated from a classic stack (PostgreSQL/Marten, EF Core, RabbitMQ/MassTransit, gRPC, Carter) to serverless AWS. Architectural decisions are recorded under `docs/adr/` — **read the relevant ADR before changing cross-cutting infrastructure**. Key recent ones: ADR-0004 (EventBridge over MassTransit/RabbitMQ), ADR-0005 (removal of in-process domain events; CDC via DynamoDB Streams), ADR-0006 (React/Next.js as the primary SPA). Use the `adr` skill to author/update ADRs.

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
```

Git hooks live in `.githooks/` (configured via `core.hooksPath`); the pre-commit hook runs `dotnet format` on staged `.cs` files and re-stages them. Don't bypass this with `--no-verify`.

Use `dotnet run --project src/AppHost/AppHost.csproj` (Aspire) to start the system locally — it provisions DynamoDB Local (`http://localhost:8000`), the Aspire AWS Lambda service emulator, and Elasticsearch/Kibana as containers, registers each Lambda function, and wires the dev environment between them. `docker-compose.yml`/`docker-compose.override.yml` are secondary/legacy to Aspire.

The Go notification service and the React SPA have their own toolchains (`go`, `pnpm`/`next`) — see those subsections.

## Architecture

### The serverless model

- **Compute** is AWS Lambda. Each use case is its own function. Locally, Aspire registers them via `AddAWSLambdaFunction<Projects.X>(...)` and runs them through the Lambda service emulator.
- **Persistence** is DynamoDB (DynamoDB Local in dev). Repositories talk to `IAmazonDynamoDB` directly (`TransactWriteItems`, etc.) — there is no EF Core `SaveChanges`/`ChangeTracker`/interceptor pipeline anymore.
- **Cross-service messaging** is Amazon EventBridge (`BuildingBlocks.Messaging/EventBridge`). The bus only exists on AWS; locally, publishes are **best-effort and fail silently**. Integration events are produced via **Change Data Capture**: a committed DynamoDB write → DynamoDB Streams → a Stream-triggered publisher Lambda → EventBridge (see ADR-0005). Do not reintroduce in-process domain events as the integration path.
- **Synchronous service-to-service calls** use direct Lambda invocation via the AWS Lambda Invoke API, not gRPC/HTTP. (The former Basket → Discount invoke was removed — Discount is now an in-process entity inside the Basket aggregate; see ADR-0012.)

### Lambda function pattern (Basket, Catalog)

Each `*.Function` project uses the **Amazon.Lambda.Annotations** source generator:

- A `partial class Functions` holds methods decorated with `[LambdaFunction]` + `[HttpApi(...)]` (or other event-source attributes). The generator emits the actual handler types referenced in the AppHost as `X.Function::X.Function.Functions_<Method>_Generated::<Method>`. When you add/rename a function method, the AppHost `lambdaHandler` string must match the generated name.
- DI is configured in a `[LambdaStartup] public class Startup.ConfigureServices(IServiceCollection)`. Configuration comes from environment variables that Aspire injects (`ConnectionStrings__*`, `services__*`, `EventBridge__*`, AWS dev credentials/region).
- Endpoints map the HTTP request to a MediatR command/query via `ISender.Send(...)` and adapt request/response DTOs (Mapster `.Adapt<T>()`, convention-based).

### CQRS conventions (Basket, Catalog, Ordering)

- Commands implement `ICommand<TResponse>`, queries implement `IQuery<TResponse>` (from `BuildingBlocks.Core`), handled by `ICommandHandler<,>`/`IQueryHandler<,>` over MediatR.
- The MediatR pipeline in Lambda startups registers `ValidationBehavior` (FluentValidation `AbstractValidator<TCommand>`) and `LoggingBehavior` from `BuildingBlocks.ServiceDefaults.Behaviors`. (The old `UnitOfWorkBehavior` is gone — there is no DB transaction scope to manage.)
- Slices live under `Features/{UseCase}/` (e.g. `Features/Products/CreateProduct`, `Features/CheckoutBasket`), each holding endpoint + command/query + handler + validator together.

### Services (src/Services/*)

- **Basket.Function** — Lambda + DynamoDB (`BasketRepository`, no caching layer; storage wired in `BasketStorageExtensions`). `ShoppingCart` is the aggregate root (`Aggregate<string>`, Id = UserName) and owns the discount rule (`ApplyDiscounts`); `Coupon` is an in-process `Entity<string>` read from the `coupons` table via `DynamoCouponRepository` — the old Discount service and its Lambda invoke were merged in (ADR-0012). Checkout publishes to EventBridge (CDC). Slices under `Features/{StoreBasket,CheckoutBasket}`.
- **Catalog.Function** — Lambda + DynamoDB (`DynamoProductRepository`, `DynamoCategoryRepository`). Slices under `Features/Products/*` and `Features/Categories/*`.
- **Ordering.Function** — single `*.Function` project like Catalog/Basket (collapsed from the old `Ordering.{Domain,Application,Infrastructure}` DDD layering). Still keeps DDD types internally (`Order` aggregate + value objects under `Models/`/`ValueObjects/`, MediatR slices under `Features/`), but persistence is **DynamoDB** (`ordering`) via `DynamoOrderRepository`. Each order is a **single item** with its `OrderItems` embedded as a list attribute — written with one `PutItem` (atomic on its own; no `TransactWriteItems`), read by `Id` with `GetItem`, listed by customer through GSI1 (`ProjectionType.ALL`, no N+1). `Aggregate<TId>`/`IAggregate` are empty aggregate-root markers (domain events removed — ADR-0005). The two CDC/event Lambdas live **inside** the Function project as plain classes (referenced by explicit handler strings in `OrderingExtensions`):
  - `EventsIntegration/Consumer/BasketCheckoutConsumerFunction` — consumes the checkout integration event and writes the order (idempotent via `ProcessedIntegrationEvents`).
  - `EventsIntegration/Publisher/OrderCreatedPublisherFunction` — DynamoDB Streams source on `ordering`; publishes `OrderCreated` to EventBridge (CDC).
  - `Ordering.DevelopmentDataSeeder` — separate Worker that creates/seeds DynamoDB tables (`DynamoTableInitializer`, in the Function project) before the Ordering Lambdas start.
- **Notification** — Go service (`src/Services/Notification/Notification.Go`), consumes events and writes to DynamoDB. Has its own `go.mod` and `main_test.go` (`go test ./...`).

### BuildingBlocks (src/BuildingBlocks/*)

Shared code referenced across services — check here before adding cross-cutting concerns:

- **BuildingBlocks.Core** — `ICommand`/`IQuery`/handler interfaces, `IUnitOfWork`, DDD base types (`Aggregate<TId>`/`IAggregate` as markers, `ValueObject`), pagination helpers (`PaginatedResult<T>`). Note: `IDomainEvent` and the domain-event machinery were deleted (ADR-0005).
- **BuildingBlocks.Messaging** — `IntegrationEvent` base record and the EventBridge integration: `AddEventBridgeMessaging()` registers `IAmazonEventBridge` + `IEventPublisher` (`EventBridgePublisher`).
- **BuildingBlocks.ServiceDefaults** — `AddServiceDefaults()` (service discovery, Polly resilience, health checks, OpenTelemetry, Serilog → Elasticsearch), the shared MediatR `Behaviors`, and `CustomExceptionHandler` for ProblemDetails responses.
- **BuildingBlocks.ServiceDefaults.Lambda** — Lambda hosting equivalents of the above.

### Orchestration and routing

- **src/AppHost** — .NET Aspire AppHost. `Program.cs` is the composition root; per-service wiring lives in `*Extensions.cs` (`BasketExtensions`, `CatalogExtensions`, `OrderingExtensions`, `ReviewExtensions`, `ObservabilityExtensions`). This is the source of truth for what infrastructure exists, Lambda handler names, `WaitFor`/`WaitForCompletion` chains, and DynamoDB Streams sources. Shared helper in `Extensions/Extensions.cs`: `WithAwsDevEnvironment()` (dummy AWS creds + region for local dev). Add new resources/functions here, not in docker-compose.
- **src/ApiGateways/YarpApiGateway** — YARP reverse proxy. Routes are prefixed per service (prefix stripped before forwarding); the ordering route has a rate-limiter policy.
- **src/WebApps/Shopping.Web.Server** — Blazor Server frontend (Refit typed clients through the gateway).
- **src/WebApps/Shopping.Web.SPA** — Angular SPA (legacy; being replaced per ADR-0006).
- **src/WebApps/Shopping.Web.SPA.React** — React/Next.js SPA (the primary SPA going forward). Uses `pnpm` (`pnpm dev` / `pnpm build` / `pnpm lint`). This directory is slated to move into its own Git repository, linked back into DuckStore as a **git submodule**.

## Testing

- Unit tests: xUnit + Moq + Moq.AutoMock, one project per service under `tests/Services/<Service>/<Service>.UnitTests` (plus `tests/BuildingBlocks/BuildingBlocks.UnitTests`). Tests target handlers directly, mocking repository/dependency interfaces.
- Functional tests (`Ordering.FunctionalTests`): use `DistributedApplicationTestingBuilder` to spin up the real Aspire app graph (including containers) and exercise actual endpoints after waiting for resource health. Slower; require Docker.
- Packages/versions are centrally pinned in `Directory.Packages.props` (central package management) — add package references there, not inline versions in `.csproj` files.
