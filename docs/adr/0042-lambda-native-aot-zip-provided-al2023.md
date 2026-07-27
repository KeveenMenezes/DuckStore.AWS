# ADR-0042: .NET Lambdas Ship as Native AOT ZIPs on `provided.al2023`

## Status
**Proposed** — July 2026

This ADR records a packaging decision that was never written down: every .NET Lambda has shipped
as a Docker container image since the serverless migration, by default rather than by choice. It
does not supersede an existing ADR. It extends [ADR-0022](./0022-lambda-production-observability.md)
by moving the mediator `Behaviors` into `BuildingBlocks.ServiceDefaults.Lambda`, which that ADR
already defines as "the Lambda-shaped subset of ServiceDefaults".

**Amended — July 2026 (phase 2 executed).** §7 originally gated Native AOT behind five blockers and
scoped it to two packages. All five were removed and AOT now applies to all eight (§8). §1–§6
describe phase 1 and still hold, except that ReadyToRun is superseded by Native AOT as the
compilation mode and `PublishTrimmed=false` (§4) no longer applies — see §8 for what replaced it.

---

## Context

Every `*.Function` project is a class library packaged into a container image
(`ecrAssets.DockerImageAsset` + `lambda.DockerImageFunction`), one image per service, with each
function selecting its handler through a Docker `cmd` override. The images land in the CDK
bootstrap repository `cdk-hnb659fds-container-assets-<account>-<region>`.

Nobody chose this. `net10.0` has **no managed Lambda runtime** — the newest is `dotnet8` — so the
project template's default (a container image) was the path of least resistance, and it stuck.

Measured on the development account (`603767527989`, `us-east-1`) in July 2026:

| Metric | Value |
|---|---|
| Images in the CDK assets repository | **128** |
| Unique layers / billed storage | 373 / **5.24 GB** |
| Images actually referenced by a live Lambda | **8** (25 layers, 0.36 GB) |
| Unreferenced images | **120 — 4.88 GB, 93% of stored bytes** |
| Compressed size of one service image | ~248 MB |

Three concrete pains follow:

- **Nothing ever gets deleted.** The repository's lifecycle policy expires only `untagged` images
  older than 365 days, but CDK tags every image with its content hash, so no image is ever
  untagged and the rule never fires. Storage grows monotonically with every deploy — a run rate of
  ~$0.52/month and rising, for a project whose DynamoDB bill is $0.
- **The base image is a moving tag.** All nine Dockerfiles used
  `FROM public.ecr.aws/lambda/dotnet:10`. AWS republishes that tag frequently, and each
  republication becomes a new permanent 134 MB layer. **16 distinct copies (2.07 GB, 40% of stored
  bytes)** accumulated in roughly one month.
- **Cold start is paid on the artifact.** A 248 MB image is the unit Lambda must fetch and
  initialize. Container images are the largest artifact the platform offers for a workload whose
  own code is a few hundred kilobytes.

There is a second, independent source of weight. Every `*.Function` project referenced
`BuildingBlocks.ServiceDefaults` — but used exactly two types from it, `ValidationBehavior` and
`LoggingBehavior`. That project also pulls Serilog, `Serilog.Sinks.Elasticsearch`,
`Aspire.Elastic.Clients.Elasticsearch`, `Microsoft.Extensions.ServiceDiscovery`,
`Microsoft.Extensions.Http.Resilience` and `OpenTelemetry.Instrumentation.AspNetCore`, none of
which a Lambda executes. A trial publish of `Catalog.Function` measured **176 MB**, of which
`Elastic.Clients.Elasticsearch.dll` alone was **69 MB**.

---

## Decision

.NET Lambdas ship as **self-contained, ReadyToRun-compiled ZIP packages on the `provided.al2023`
custom runtime**, arm64. Container images and ECR leave the .NET deployment path entirely.

Native AOT — which would cut init further — is **deferred, not rejected**; §7 defines when and
where it applies.

### 1. One ZIP per service, dispatched by `ANNOTATIONS_HANDLER`

The property that made the container model attractive is preserved exactly: **one artifact per
service backs every function in it**. `Amazon.Lambda.Annotations` emits a `Program.Main` that
dispatches on an environment variable, so the Docker `cmd` override becomes an env var.

This is opt-in, via an assembly attribute. Every `*.Function` project that hosts at least one
`[LambdaFunction]` MUST declare it:

```csharp
// Shared/Configuration/AssemblyAttributes.cs
[assembly: LambdaGlobalProperties(GenerateMain = true)]
```

The generator then produces:

```csharp
public class GeneratedProgram
{
    public static async Task Main(string[] args)
    {
        switch (Environment.GetEnvironmentVariable("ANNOTATIONS_HANDLER"))
        {
            case "ProductStreamPublisher":
                Func<DynamoDBEvent, Task> handler =
                    new Catalog.Function.Functions_ProductStreamPublisher_Generated().ProductStreamPublisher;
                await LambdaBootstrapBuilder.Create(handler, new DefaultLambdaJsonSerializer())
                    .Build().RunAsync();
                break;
            // ... one case per [LambdaFunction] method in the service
        }
    }
}
```

The `ANNOTATIONS_HANDLER` value is the **method name**, not the three-part handler string.

### 2. Required project properties

`provided.al2023` executes a file literally named `bootstrap`, which is what `AssemblyName`
produces:

```xml
<OutputType>Exe</OutputType>
<AssemblyName>bootstrap</AssemblyName>
<RootNamespace>Catalog.Function</RootNamespace>
<PublishReadyToRun>true</PublishReadyToRun>
<PublishTrimmed>false</PublishTrimmed>
```

`SelfContained` and `RuntimeIdentifier` MUST NOT be set in the `.csproj`. Setting them there makes
every `*.UnitTests` project that references the function fail with **NETSDK1151** ("a self-contained
executable cannot be referenced by a non self-contained executable"). They are passed by the
publish command instead — the only place a deployable artifact is produced.

**Incorrect** — breaks the test projects:

```xml
<SelfContained>true</SelfContained>
<RuntimeIdentifier>linux-arm64</RuntimeIdentifier>
```

**Correct** — `infra/constructs/dotnet-lambda-code.ts`:

```
dotnet publish <project> -c Release -r linux-arm64 --self-contained true -p:PublishReadyToRun=true -o <out>
```

### 3. Lambda packages MUST NOT reference `BuildingBlocks.ServiceDefaults`

`ValidationBehavior`/`LoggingBehavior` move to
`BuildingBlocks.ServiceDefaults.Lambda/Behaviors/`, namespace
`BuildingBlocks.ServiceDefaults.Lambda.Behaviors`. `BuildingBlocks.ServiceDefaults` remains for the
`*.DevelopmentDataSeeder` workers, which are long-lived hosts and genuinely use it.

A `*.Function` project MUST reference `BuildingBlocks.ServiceDefaults.Lambda` and MUST NOT
reference `BuildingBlocks.ServiceDefaults`. It also MUST NOT declare
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` — that reference assumed a managed
runtime whose shared framework is already on the host, which is not true of a self-contained
package. The `Microsoft.Extensions.*` packages a function actually uses are declared explicitly.

Measured effect on `Catalog.Function`:

| Stage | Published size |
|---|---|
| With `ServiceDefaults` + `FrameworkReference` | 176 MB |
| After both removed | **99 MB** (37 MB zipped) |
| Container image it replaces | 248 MB compressed |

### 4. Trimming stays off

`PublishTrimmed` MUST remain `false`. MediatR (`RegisterServicesFromAssembly`) and FluentValidation
(`AddValidatorsFromAssembly`) discover handlers and validators by scanning the assembly at runtime.
The trimmer cannot see those references and would remove the types, producing a package that builds
cleanly and fails at invocation. This is the same constraint that gates Native AOT (§7).

### 5. CDK: one shared bundling helper

`infra/constructs/dotnet-lambda-code.ts` exports `dotnetLambdaCode(serviceDir, projectPath)` plus
`DOTNET_ARCH` and `DOTNET_RUNTIME`. It replaces the nine duplicated `DockerImageAsset` blocks.

Bundling prefers the host SDK and falls back to the `mcr.microsoft.com/dotnet/sdk:10.0` container,
so `cdk deploy` behaves the same on a laptop and on a CI runner. CI workflows for stacks containing
.NET Lambdas MUST include an `actions/setup-dotnet` step, or every deploy pays for the container
fallback.

The asset hash covers the service directory, `src/BuildingBlocks`, `Directory.Packages.props` and
`nuget.config` — the same inputs the Dockerfiles listed in their `exclude` globs. Hashing the repo
root instead would redeploy all eight services on every unrelated commit.

```typescript
this.streamPublisher = new lambda.Function(this, 'StreamPublisher', {
  functionName: 'catalog-products-stream-publisher',
  runtime: DOTNET_RUNTIME,          // provided.al2023
  handler: 'bootstrap',             // inert on a custom runtime, but required by CFN
  architecture: DOTNET_ARCH,        // arm64
  code: catalogCode,                // the shared per-service ZIP
  environment: {
    ANNOTATIONS_HANDLER: 'ProductStreamPublisher',
    EventBridge__BusName: eventBus.eventBusName,
  },
});
```

### 6. Base-image drift is designed out

The 2.07 GB of duplicated base layers was a symptom of pinning a *moving* tag. The ZIP path removes
the runtime base image from the deployment artifact altogether. The SDK image now appears only as a
CI-time build tool, where a stale copy cannot accumulate storage cost.

### 7. Native AOT is scoped to the Basket and Pricing **packages**, and is gated

Of the 25 functions, **19 are asynchronous** — DynamoDB Streams publishers and EventBridge
consumers — where init latency is invisible to any user. Six are synchronous and user-facing:
`basket-checkout-basket`, `basket-merge-basket`, `pricing-get-installment-plan`,
`pricing-get-basket-installment-plan`, `pricing-create-campaign`, `pricing-end-campaign`.

**The unit of AOT is the package, not the function.** §1 makes one ZIP back every function in a
service, so "AOT the six synchronous functions" is not expressible: those six live in exactly two
services, and compiling them ahead-of-time compiles everything shipped alongside them.

| Service | Functions in package | Of which synchronous | Async functions that ride along |
|---|---|---|---|
| Basket | 3 | 2 | `basket-shopping-carts-stream-publisher` |
| Pricing | 6 | 4 | `pricing-product-deleted-consumer`, `pricing-prices-stream-publisher` |

So the real scope is **9 functions across 2 packages**. The remaining six services (Catalog,
CatalogView, Ordering, Payment, PaymentGateway, Review) are 100% asynchronous and MUST stay on
ReadyToRun — there is no user-perceived latency there to buy.

Native AOT MAY be adopted for the Basket and Pricing packages, and MUST NOT be adopted elsewhere,
because each of the following is a prerequisite and none is satisfied today:

| Blocker | Status | Scope in Basket + Pricing |
|---|---|---|
| MediatR 14.0.0 assembly scanning | No source generator in the package; `RegisterServicesFromAssembly` must become explicit registration | 6 handlers |
| FluentValidation 12.1.1 assembly scanning | Same, for `AddValidatorsFromAssembly` | 6 validators |
| Mapster 7.4.0 `.Adapt<T>()` | Uses `Reflection.Emit`, unsupported under AOT — must move to `Mapster.Tool` or hand-written mapping | 11 call sites in 6 files — **every one of them is in a synchronous endpoint of these two services**, and no asynchronous function uses Mapster |
| `DefaultLambdaJsonSerializer` | Must become `SourceGeneratorLambdaJsonSerializer<T>` with a `JsonSerializerContext` | 2 contexts |
| `PublishTrimmed=false` (§4) | `PublishAot=true` implies trimming, so §4's rule is incompatible with AOT and is exactly what the four rows above pay for | — |
| CI runners | Native AOT does not cross-compile x64 → arm64; ReadyToRun does, which is why phase 1 needed no runner change | `ubuntu-24.04-arm` on 2 workflows (`deploy-basket-cdk.yml`, `deploy-pricing-cdk.yml`) — not all nine |

**Neither MediatR 14.0.0 nor FluentValidation 12.1.1 carries any trim/AOT annotation**
(`RequiresUnreferencedCode`, `DynamicallyAccessedMembers`, `RequiresDynamicCode`). The consequence
is sharper than "it needs work": the compiler emits **no IL2xxx/IL3xxx diagnostic** pointing at the
scanning calls, so an AOT build of these packages succeeds silently and fails at invocation with an
unresolved handler. Phase 2 therefore MUST be validated by invoking every migrated function against
a published AOT artifact, not by a green build.

Promotion of a package to Native AOT MUST be recorded as an amendment to this ADR, listing the
functions moved and confirming each blocker above is resolved for them.

---

### 8. Phase 2 — Native AOT for all eight packages

Every blocker in §7 was removed by replacing the offending library rather than working around it,
which changed the answer to "which packages" from *two* to *all eight*: once nothing discovers
anything by reflection, there is no package-by-package risk left to weigh.

| §7 blocker | Resolution |
|---|---|
| MediatR 14.0.0 assembly scanning | Replaced by **Mediator 3.0.2** (martinothamar) — dispatch is source-generated, `AddMediator()` is generated registration, nothing is scanned. Handlers return `ValueTask<T>`; pipeline `next` takes `(message, ct)` |
| FluentValidation 12.1.1 assembly scanning | Replaced by a project-owned `IValidator<T>` (`BuildingBlocks.Core/Validation`) with hand-written `Validate` methods, registered explicitly |
| Mapster 7.4.0 `Reflection.Emit` | Removed; all 11 call sites are explicit construction |
| `DefaultLambdaJsonSerializer` | Replaced by `SourceGeneratorLambdaJsonSerializer<T>` over a per-service `JsonSerializerContext`, plus `MessagingSerializerContext` for integration events |
| CI cannot cross-compile | The seven service deploy workflows moved to `ubuntu-24.04-arm` |

**MiniValidation was evaluated and rejected** as the FluentValidation replacement. It is 10× smaller
(52 KB vs 519 KB) and would have cut startup cost, but it carries **no trim/AOT annotations** and
reaches for `GetProperties`, `GetCustomAttributes` and `MakeGenericType`/`MakeGenericMethod`. Under
Native AOT an unrooted generic instantiation throws `PlatformNotSupportedException` at runtime, so
it would have moved the hazard rather than removed it — and the recursion into
`IReadOnlyList<BasketInstallmentItem>` in `GetBasketInstallmentPlanQueryValidator` is exactly that
shape. A hand-written interface has no such failure mode: the compiler sees every rule.

Required project properties, replacing §2's ReadyToRun line and §4's trimming rule:

```xml
<IsAotCompatible>true</IsAotCompatible>
<OptimizationPreference>Speed</OptimizationPreference>
<InvariantGlobalization>true</InvariantGlobalization>
```

`PublishAot` and `RuleIdentifier` stay out of the `.csproj` for the same NETSDK1151 reason §2 gives;
the publish command passes them. `IsAotCompatible` is the load-bearing one: it turns on the
trim/AOT analyzers at ordinary build time, which is the only defence against §7's "builds clean,
fails at invocation" failure mode. It earned its place immediately — it caught
`EventBridgePublisher.PublishAsync<T>` and five `CatalogView` sync strategies still calling
reflection-based `JsonSerializer`, every one of which would have thrown on first invocation.

Native AOT links a native binary and therefore **cannot cross-compile**, neither across
architectures nor across operating systems. Bundling consequently prefers the container
(`infra/docker/dotnet-aot`, SDK + clang + zlib) and uses the host SDK only on linux-arm64. The
image is built once and cached rather than installing the toolchain per service, and it needs no
root at bundle time — CDK runs bundling as the calling user.

Measured across the eight packages:

| | Container image (before ADR) | Phase 1 — self-contained + R2R | Phase 2 — Native AOT |
|---|---|---|---|
| Files in artifact | — | 250 | **1** (`bootstrap`) |
| Unpacked | 248 MB compressed image | 99 MB | **~14 MB** |
| Uploaded (zipped) | 248 MB | 37 MB | **6.1 MB** |

`InvariantGlobalization` is a behavioural change, not just a size one: culture-aware string
comparison and formatting fall back to the invariant culture. These services format decimals and
compare ordinal identifiers only, so this is safe today — a future feature that formats currency
per locale MUST revisit it.

**Validation is by artifact, not by build.** Because §7's failure mode is invisible at compile time,
a change touching serialization, DI registration or a `[LambdaFunction]` signature MUST be checked
with a real AOT publish (`-p:PublishAot=true` in the arm64 container) and confirmed to emit zero
IL2xxx/IL3xxx warnings. All eight packages are clean at the time of writing.

---

## Applies To

- `src/Services/{Basket,Catalog,CatalogView,Ordering,Payment,PaymentGateway,Pricing,Review}/*.Function`
  — 25 Lambda functions across 8 executable packages.
- `src/Services/User/User.Function` — **stays a class library.** It hosts no `[LambdaFunction]`;
  `myProfile`/`updateProfile` are AppSync direct DynamoDB resolvers
  ([ADR-0009](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)), and
  `UserStack` deploys no Lambda. Its `AppHost.csproj` reference carries
  `IsAspireProjectResource="false"`.
- `src/BuildingBlocks/BuildingBlocks.ServiceDefaults{,.Lambda}` — the `Behaviors` move of §3.
- `infra/constructs/{basket,catalog,catalogview,ordering,payment,pricing,review}-lambdas.ts` and the
  new `dotnet-lambda-code.ts`.
- `.github/workflows/deploy-{basket,catalog,catalogview,ordering,payment,pricing,review}-cdk.yml`.
- Not applicable to `src/Services/ProductImages` (TypeScript `NodejsFunction`) or
  `src/Services/Notification` (Go).

---

## Consequences

### Positive

- **ECR leaves the .NET path.** No image is pushed by any service deploy, so the repository stops
  growing. Combined with `cdk gc`, ECR storage for this account goes to zero.
- **The deployment artifact shrinks 40×** — 248 MB compressed image → 6.1 MB ZIP holding a single
  `bootstrap` binary — which is the bytes Lambda fetches before it can initialize.
- **There is no runtime to start.** Native AOT removes JIT and assembly loading from init entirely;
  the ZIP also no longer carries the 89 MB Elasticsearch client that was never executed.
- **The nine service Dockerfiles are deleted.** Docker survives only as a build toolchain
  (`infra/docker/dotnet-aot`), never as a deployment artifact.
- **Reflection is gone from the request path** — source-generated dispatch, hand-written
  validators, source-generated JSON — so the analyzers can prove what used to be a runtime gamble.
- **One helper replaces nine near-identical asset blocks**, so packaging policy changes in one file.
- **The one-artifact-per-service property is preserved**, so no service was split or merged and the
  `<service>-<resource>-stream-publisher` naming convention is untouched.

### Negative / Costs

- **Failures move from compile time to invocation time.** A missing `[JsonSerializable]` or an
  unregistered handler builds clean and throws on the first request. §8's artifact-level validation
  rule is the only thing standing between that and production.
- **Builds are much slower.** Native AOT links a real binary per service, and no cross-compilation
  means every deploy runs in an arm64 container. A full eight-package build is minutes, not seconds.
- **CI is pinned to arm64 runners** (`ubuntu-24.04-arm`) for the seven service workflows.
- **Three libraries left the codebase** — MediatR, FluentValidation and Mapster — so the project no
  longer benefits from their ecosystems, and their idioms (fluent rules, convention mapping) are now
  hand-written code that can drift.
- **`InvariantGlobalization` changes behaviour**, not just size: culture-aware formatting and
  comparison fall back to the invariant culture.
- **`AssemblyName` is now `bootstrap` for eight projects**, so the assembly no longer matches the
  project name. Aspire's Lambda emulator resolves handlers by reflection over that name, and its
  strings had to change from `Catalog.Function::…` to `bootstrap::…`. **This drift is silent**: a
  stale string fails inside `LambdaBootstrap.InitializeAsync`, and the only surfaced symptom is a
  `RuntimeApiClientException` thrown by the test tool while *reporting* the failure — the real
  cause never appears. Eight of the 24 registrations were missed on the first pass for exactly this
  reason. The handler string is therefore composed by `Extensions.LambdaHandler(namespace, method)`
  rather than written out, so the assembly name exists in one place.
- **`Amazon.Lambda.Core` was bumped 2.8.1 → 3.1.1**, the floor for `Amazon.Lambda.RuntimeSupport`
  2.0.0. This is a solution-wide central pin, so it also moves the seeders and test projects.
- **CI depends on a `setup-dotnet` step.** Without it a deploy still succeeds, but silently falls
  back to container bundling and gets much slower.
- **Cold-start improvement is real but bounded.** Most of the remaining init is JIT/runtime startup
  that ReadyToRun only partially removes; the step change needs §7.

### Mitigation Strategies

- `IsAotCompatible=true` surfaces trim/AOT problems as build warnings instead of runtime faults;
  it is the reason six latent serialization bugs were found before deployment rather than after.
- `EventBridgePublisher` throws an exception naming the unregistered event type rather than failing
  opaquely, so the most likely future mistake explains itself.
- Bundling always works: the container path needs no host SDK at all, and the host-SDK fast path is
  taken only where it is known to be correct (linux-arm64).
- The custom asset hash scopes rebuilds to the service plus BuildingBlocks, so the loss of Docker
  layer caching does not translate into eight full rebuilds per commit.
- `PublishTrimmed=false` is stated as a binding rule in §4 precisely because enabling it produces a
  package that builds clean and fails only at invocation.

### Future Constraints

- New `*.Function` projects MUST follow §1–§3 and §8: `GenerateMain`, `AssemblyName=bootstrap`, no
  `ServiceDefaults` reference, no `FrameworkReference`, `IsAotCompatible`.
- Any type crossing a Lambda boundary MUST be added to that service's `JsonSerializerContext`, and
  any new integration event to `MessagingSerializerContext`.
- New dependencies MUST be checked for trim/AOT annotations before adoption. A library that
  discovers types by reflection — the shape of MediatR, FluentValidation and MiniValidation alike —
  cannot be used in a Lambda package without reopening §7.
- New Lambdas MUST be declared with `dotnetLambdaCode(...)` and an `ANNOTATIONS_HANDLER` env var.
  Reintroducing `DockerImageFunction` for a .NET service reopens the ECR growth this ADR closes and
  requires superseding it.
- Adding a dependency to `BuildingBlocks.ServiceDefaults.Lambda` adds it to **every** Lambda ZIP.
  Dependencies that only make sense on a long-lived host belong in `BuildingBlocks.ServiceDefaults`.

---

## References

- [ADR-0009: AppSync Resolver Selection](./0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md)
  — why `User.Function` hosts no Lambda.
- [ADR-0019: Module-Oriented Service Structure and Rule-Based Stream Publishers](./0019-module-oriented-service-structure-and-rule-based-stream-publishers.md)
  — the per-service function inventory this repackages.
- [ADR-0022: Production Observability for Lambda Functions](./0022-lambda-production-observability.md)
  — defines `BuildingBlocks.ServiceDefaults.Lambda`, extended by §3.
- [AWS Lambda: .NET custom runtimes on `provided.al2023`](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package.html)
- [`Amazon.Lambda.Annotations` — executable assembly / `GenerateMain`](https://github.com/aws/aws-lambda-dotnet/tree/master/Libraries/src/Amazon.Lambda.Annotations)
- [NETSDK1151 — self-contained project references](https://aka.ms/netsdk1151)
