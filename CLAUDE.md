# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

DuckStore is a .NET microservices e-commerce sample built around .NET Aspire orchestration, Vertical Slice Architecture, and CQRS. It's a learning/demo project, not production software — favor consistency with existing patterns over introducing new ones.

## Common commands

```bash
# Build the whole solution
dotnet build DuckStore.sln

# Run everything (Aspire orchestrates all services, infra containers, and web apps)
dotnet run --project src/AppHost/AppHost.csproj

# Run all tests
dotnet test

# Run a single test project
dotnet test tests/Services/Basket/Basket.UnitTests/Basket.UnitTests.csproj

# Run a single test by name
dotnet test --filter "FullyQualifiedName~CheckoutBasketCommandHandlerTests"

# Format code (also runs automatically on staged .cs files via .githooks/pre-commit)
dotnet format DuckStore.sln
```

Git hooks live in `.githooks/` (configured via `core.hooksPath`); the pre-commit hook runs `dotnet format` on staged `.cs` files and re-stages them. Don't bypass this with `--no-verify`.

Use `dotnet run --project src/AppHost/AppHost.csproj` (Aspire) to start the system locally — it provisions Postgres, Redis, RabbitMQ, Elasticsearch/Kibana, and LocalStack (SQS/DynamoDB) as containers and wires service discovery between them. `docker-compose.yml`/`docker-compose.override.yml` are secondary/legacy to Aspire.

## Architecture

### Services (src/Services/*)

Each service follows **Vertical Slice Architecture**: features are organized by use case folder (not by technical layer), each containing an endpoint, command/query, handler, and validator together.

- **Basket.API** — Carter + MediatR + Marten (PostgreSQL document store) with a Redis cache-aside decorator (`CacheBasketRepository` wraps `BasketRepository`). Calls Discount.Grpc to apply discounts at checkout. Slices live under `Basket/{CheckoutBasket,StoreBasket,GetBasket,DeleteBasket}`.
- **Catalog.API** — Same Carter + MediatR + Marten/PostgreSQL pattern as Basket. Slices under `Features/Products/*` and `Features/Categories/*`.
- **Discount.Grpc** — Plain gRPC service (no Carter/MediatR), EF Core + SQLite.
- **Ordering.{Domain,Application,Infrastructure,API}** — The one service using classic DDD/layered architecture instead of vertical slices: `Ordering.Domain` has the `Order` aggregate, value objects, and domain events; `Ordering.Application` wires MediatR + pipeline behaviors; `Ordering.Infrastructure` has the EF Core `ApplicationDbContext` (PostgreSQL) with MassTransit inbox/outbox and `SaveChangesInterceptor`s for auditing and domain event dispatch. `Ordering.MigrationService` applies EF Core migrations before `Ordering.API` starts.
- **Notification** — Go service (not .NET) consuming events from SQS and writing to DynamoDB via LocalStack in dev.

### CQRS conventions (Basket, Catalog, Ordering)

- Commands implement `ICommand<TResponse>`, queries implement `IQuery<TResponse>` (from `BuildingBlocks.Core`), handled by `ICommandHandler<,>`/`IQueryHandler<,>` over MediatR.
- A shared MediatR pipeline (registered per-service) runs `ValidationBehavior` (FluentValidation `AbstractValidator<TCommand>`), `LoggingBehavior`, and `UnitOfWorkBehavior` in that order for every request.
- Carter `ICarterModule` endpoint classes map HTTP routes directly to `ISender.Send(...)`. Request/response DTOs convert to/from commands via Mapster's `.Adapt<T>()` (convention-based, no explicit profiles).

### BuildingBlocks (src/BuildingBlocks/*)

Shared code referenced by every service — check here before adding cross-cutting concerns, they likely already exist:

- **BuildingBlocks.Core** — `ICommand`/`IQuery`/handler interfaces, `IUnitOfWork`, DDD base types (`Aggregate<TId>`, `IDomainEvent`, `ValueObject`), pagination helpers.
- **BuildingBlocks.Messaging** — `IntegrationEvent` base record and the MassTransit/RabbitMQ `AddMessageBroker()` extension (auto-registers consumers from the calling assembly, kebab-case endpoint naming).
- **BuildingBlocks.ServiceDefaults** — `AddServiceDefaults()` (service discovery, Polly resilience, health checks, OpenTelemetry, Serilog → Elasticsearch logging), the shared MediatR behaviors, and `CustomExceptionHandler` for ProblemDetails responses. Every API's `Program.cs` calls this.
- **BuildingBlocks.ServiceDefaults.Lambda** — AWS Lambda hosting equivalents of the above, used by the Go/Lambda-adjacent notification path.

### Orchestration and routing

- **src/AppHost** — .NET Aspire AppHost (`Program.cs`). This is the source of truth for what infrastructure exists, service dependencies, and health-check wait chains (e.g. `basket-api` waits on `redis`, `basketDb`, `discount-api`, the RabbitMQ broker, and Elasticsearch before starting). Add new resources/services here, not in docker-compose.
- **src/ApiGateways/YarpApiGateway** — YARP reverse proxy. Routes are prefixed per service (`/catalog-service/**`, `/basket-service/**`, `/ordering-service/**`, the prefix stripped before forwarding); the ordering route has a rate-limiter policy applied.
- **src/WebApps/Shopping.Web.Server** — Blazor Server frontend, talks to services through Refit-generated typed clients (`ICatalogService`, `IBasketService`, `IOrderingService`) pointed at the YARP gateway.
- **src/WebApps/Shopping.Web.SPA** — Angular SPA, also calls through the gateway.

## Testing

- Unit tests: xUnit + Moq + Moq.AutoMock, one project per service under `tests/Services/<Service>/<Service>.UnitTests`. Tests target handlers directly (e.g. `CheckoutBasketCommandHandlerTests`), mocking repository/unit-of-work dependencies.
- Functional tests (`Ordering.FunctionalTests`): use `DistributedApplicationTestingBuilder` to spin up the real Aspire app graph (including containers) and exercise actual HTTP endpoints via `_app.CreateHttpClient("ordering-api")` after waiting for resource health. These are slower and require Docker.
- Packages and versions are centrally pinned in `Directory.Packages.props` (central package management) — add new package references there, not inline versions in `.csproj` files.
