# ADR-0013: Remove All Basket Caching — Redis Cache-Aside and DynamoDB DAX

## Status
**Accepted** — June 2026

---

## Context

`Basket` was the only service in DuckStore with a caching layer, and it carried **two**
independent caching implementations selected by environment:

- **Non-production: Redis cache-aside.** `CacheBasketRepository` decorated `BasketRepository`
  (via `Scrutor` `.Decorate(...)`), reading/writing a serialized cart in Redis around the
  DynamoDB access. Aspire provisioned a `redis` container (`AddRedis("redis")`) and injected
  `ConnectionStrings__redis`; `docker-compose` had an equivalent `distributedcache` service.
- **Production: DynamoDB DAX.** `BasketStorageExtensions` swapped `IAmazonDynamoDB` for a
  `ClusterDaxClient` (a drop-in that implements `IAmazonDynamoDB`), giving transparent
  read/write-through caching. The CDK stack (`infra/constructs/basket-lambdas.ts`) provisioned
  a full DAX topology: a `CfnCluster` (`dax.t3.small`), a subnet group, two security groups, a
  DAX IAM service role, and put the `store-basket` / `checkout-basket` Lambdas **inside the VPC**
  (public subnets, `allowPublicSubnet`) with `dax:*` IAM actions and `Dax__Endpoint`/`Dax__Port`
  env vars.

This bought little and cost a lot for a learning/demo project:

- **Two caching stacks for one aggregate.** The same repository had two divergent code paths
  (`IsProduction` branch) plus a decorator, doubling the surface to reason about and test.
- **DAX is expensive and heavy.** A DAX cluster runs continuously (per-node cost) and forces the
  Basket Lambdas into a VPC — adding cold-start ENI attachment, subnet/security-group management,
  and an `AWS::DAX::*` resource class — to accelerate what are already single-digit-millisecond
  `GetItem`/`PutItem` calls on a tiny dataset.
- **A cache that doesn't match the workload.** The cart is written on nearly every user action
  (`StoreBasket`) and read once at checkout; a write-heavy, per-user key gets little benefit from
  either cache, and correctness (checkout reading the latest cart) matters more than latency here.
- **Extra dependencies and moving parts.** `AWSSDK.DAX.Client`, `Aspire.StackExchange.Redis`,
  `Aspire.Hosting.Redis`, `OpenTelemetry.Instrumentation.StackExchangeRedis`, and `Scrutor` all
  existed solely to serve caching, as did a Redis container developers had to run locally.

This is a persistence/infrastructure-policy decision, so it warrants an ADR. It revises the
caching design that was introduced directly in the Basket CDK stack and storage wiring (commits
`6fb3c73` and the DAX addition to `basket-lambdas.ts`) without a prior ADR.

---

## Decision

Remove **all** Basket caching. `Basket` MUST read and write DynamoDB directly, with no cache, in
every environment.

### 1. One cacheless persistence path

`BasketStorageExtensions.AddBasketStorage` registers `BasketRepository` over a single, plain
`AmazonDynamoDBClient`. The `IsProduction` branch, the DAX client factory, and the Redis
decorator are all removed. Locally the SDK resolves DynamoDB Local via the injected
`AWS_ENDPOINT_URL_DYNAMODB`; on AWS it uses the Lambda role's credentials/region.

```csharp
// Correct — one path, no cache, both environments.
services.AddScoped<IBasketRepository, BasketRepository>();
services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
```

```csharp
// Incorrect — environment-branched caching (removed).
if (IsProduction(configuration))
    services.AddSingleton(_ => CreateDaxClient(configuration)); // DAX
else
{
    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(...));
    services.Decorate<IBasketRepository, CacheBasketRepository>();  // Redis cache-aside
}
```

`CacheBasketRepository` is deleted. `Scrutor`, `AWSSDK.DAX.Client`, and the Redis/OTel-Redis
packages are removed from `Directory.Packages.props` and the project files, since caching was
their only use.

### 2. Basket Lambdas leave the VPC and grant DynamoDB directly

The `store-basket` and `checkout-basket` functions MUST run outside any VPC with **direct**
DynamoDB permissions. The DAX cluster, subnet group, security groups, DAX IAM role, `dax:*`
policy statements, VPC placement, and `Dax__Endpoint`/`Dax__Port`/`DOTNET_ENVIRONMENT=Production`
env vars are all removed from `infra/constructs/basket-lambdas.ts`.

> **Correctness note:** these Lambdas previously received table access *through the DAX service
> role* (`shoppingCartsTable.grantReadWriteData(daxRole)`), not directly. Removing DAX therefore
> **requires** granting the tables to the functions themselves, or they fail with AccessDenied:
> `shoppingCartsTable.grantReadWriteData(fn)` + `couponsTable.grantReadData(fn)` for both
> `store-basket` and `checkout-basket`.

### 3. No Redis anywhere

`AddRedis("redis")` and the `.WaitFor(redis)`/`.WithReference(redis)` wiring are removed from the
AppHost (`Program.cs`, `BasketExtensions.cs`), along with the `redis` resource and
`ConnectionStrings__redis` in `manifest.json` and the `distributedcache` service in the legacy
`docker-compose` files.

---

## Applies To

- `src/Services/Basket/Basket.Function` (`BasketStorageExtensions`, `Startup`, `GlobalUsings`,
  `BasketSerializer`; **removed:** `Data/CacheBasketRepository.cs`)
- `src/AppHost` (`Program.cs`, `BasketExtensions.cs`, `AppHost.csproj`, `manifest.json`)
- `infra` (`constructs/basket-lambdas.ts`, `stacks/basket-stack.ts`, `bin/app.ts`)
- `Directory.Packages.props`, `Basket.Function.csproj`
- `docker-compose.yml`, `docker-compose.override.yml`

---

## Consequences

### Positive
- One persistence path for Basket across all environments — no `IsProduction` branch, no
  decorator, less code to test and reason about.
- No always-on DAX cluster and no VPC for the Basket Lambdas: lower AWS cost, fewer
  `AWS::DAX::*`/networking resources, and no VPC cold-start ENI penalty.
- Five caching-only dependencies (`AWSSDK.DAX.Client`, `Aspire.StackExchange.Redis`,
  `Aspire.Hosting.Redis`, `OpenTelemetry.Instrumentation.StackExchangeRedis`, `Scrutor`) and one
  local Redis container are gone.
- Checkout always reads the latest committed cart directly from DynamoDB (no cache-coherency
  window to reason about).

### Negative / Costs
- Every read/write now hits DynamoDB. For this sample's tiny, per-user, write-heavy cart this is
  negligible, but the service loses a ready-made read accelerator.
- If Basket ever became read-heavy at scale (e.g. a shared/aggregated cart view), a caching layer
  would have to be reintroduced deliberately.
- The DAX/Redis design described in earlier commit history and `CLAUDE.md` is now historical.

### Mitigation Strategies
- `BasketRepository` keeps `IAmazonDynamoDB` behind `IBasketRepository`, so a future cache can be
  reintroduced as a decorator (Redis) or a client swap (DAX) without touching handlers — exactly
  as it was wired before, but only if a real read-heavy workload justifies it.
- Keep DynamoDB access patterns key-based (`GetItem`/`PutItem` by `UserName`) so latency stays low
  without a cache.

---

## References
- [ADR-0012: Merge Discount into Basket — Coupon as an In-Process Entity](./0012-merge-discount-into-basket-coupon-as-in-process-entity.md)
- [ADR-0008: Extend CDC Event Publishing to the Basket shopping-carts Stream Publisher](./0008-extend-cdc-event-publishing-basket-shoppingcarts-stream-publisher.md)
- [ADR-0005: Remove In-Process Domain Events — Integration Events via DynamoDB Streams (CDC)](./0005-remove-domain-events-cdc-via-dynamodb-streams.md)
- [ADR-0000: Official Architecture Decision Records Standard](./0000-official-architecture-decisios-records-standard.md)
