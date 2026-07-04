---
name: service-architecture
description: Use when implementing or migrating a DuckStore Lambda service (Basket, Catalog, Review, User) to the Ordering reference architecture — module-per-aggregate layout, ValueObjects from BuildingBlocks, rule-based DynamoDB Streams publishers, event Lambdas as [LambdaFunction] methods with [LambdaStartup] DI, and AppSync direct-DynamoDB resolvers for simple operations. Ordering is the finished reference implementation to copy from.
---

# DuckStore service architecture (Ordering is the reference)

**Copy the patterns from `src/Services/Ordering/Ordering.Function`.** It is the finished
implementation of everything below. When unsure, open the matching Ordering file and mirror it.

Related: `docs/adr/0019-*.md` (module layout + publishers), `docs/adr/0009-*.md` (resolver
selection), skills `resolver-selection` and `adr`.

## Migration order (do these steps in sequence)

1. **Restructure** into `Modules/<PluralAggregate>/` + `Shared/`.
2. **Domain** — Entities, ValueObjects (extend BuildingBlocks `ValueObject`), Enums, Dtos.
3. **Data** — repository under the module.
4. **Classify every operation** (ADR-0009): simple read/delete/write → **AppSync direct
   DynamoDB resolver, NO Lambda**; complex logic → keep a Lambda `[LambdaFunction]`.
5. **EventsIntegration** — Consumers, and Publishers (rule-based if the publisher emits 2+
   event types / reacts to a state transition; otherwise a single plain handler).
6. **Event Lambdas** (consumer + stream publisher) = `[LambdaFunction]` methods on
   `partial class Functions`, DI from `[LambdaStartup] Startup` via `[FromServices]`.
7. **Update handler strings** in AppHost + CDK + launchSettings, then **verify** them against
   the generated code.
8. `dotnet build` + `dotnet test` + `dotnet format`.

---

## 1. Folder layout

```
<Service>.Function/
├── Shared/
│   ├── Configuration/   ServiceRegistration.cs, GlobalUsings.cs, AssemblyAttributes.cs
│   ├── Data/            ProcessedIntegrationEvent.cs   (idempotency inbox)
│   └── Exceptions/      shared domain exceptions
├── Modules/
│   └── <PluralAggregate>/            e.g. Orders, Products, Baskets
│       ├── Data/                     repository + IRepository
│       ├── Features/<Action>/        Endpoint.cs, Handler.cs, Validator.cs?, Mapper.cs?
│       ├── EventsIntegration/
│       │   ├── Consumers/<Event>/    Endpoint.cs, Handler.cs, Mapper.cs, Command.cs
│       │   └── Publishers/           Rules/<X>Rule.cs, <Agg>StreamImage.cs, <Agg>StreamPublisherFunction.cs
│       └── Domain/{Entities, ValueObjects, Enums, Dtos}
└── Startup.cs           [LambdaStartup] -> Shared/Configuration/ServiceRegistration
```

**RULE: module names are PLURAL** (`Orders`, `Products`). A singular folder `Order` makes a
namespace segment `…Modules.Order` that shadows the `Order` **type** → build error
`CS0118: 'Order' is a namespace but is used like a type`.

**RULE: namespaces match folders**, EXCEPT the `partial class Functions` (see §7) which stays
in the **root** namespace `<Service>.Function`.

---

## 2. Domain — Value Objects extend BuildingBlocks `ValueObject`

Base types live in `src/BuildingBlocks/BuildingBlocks.Core/DomainModel/ValueObject.cs`.

**Single-value (Id, name)** → `ValueObject<T>` (`where T : IComparable<T>`, exposes `.Value`).
Reference: `Modules/Orders/Domain/ValueObjects/OrderId.cs`.
```csharp
using BuildingBlocks.Core.DomainModel;

namespace <Service>.Function.Modules.<Plural>.Domain.ValueObjects;

public class <Name>Id : ValueObject<Guid>
{
    private <Name>Id(Guid value) : base(value) { }

    public static <Name>Id Of(Guid value)
    {
        if (value == Guid.Empty)
            throw new <Name>IdBadRequestException(value);
        return new <Name>Id(value);
    }
}
```

**Multi-field** → non-generic `ValueObject` with `GetEqualityComponents()`.
Reference: `Modules/Orders/Domain/ValueObjects/Address.cs`.
```csharp
using BuildingBlocks.Core.DomainModel;

namespace <Service>.Function.Modules.<Plural>.Domain.ValueObjects;

public class <Name> : ValueObject
{
    public string Foo { get; } = default!;
    // ... more properties

    protected <Name>() { }
    private <Name>(string foo /*...*/) { Foo = foo; /*...*/ }

    public static <Name> Of(string foo /*...*/)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foo);   // validate
        return new <Name>(foo /*...*/);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Foo;
        // ... one yield per field
    }
}
```

Always: `private` ctor + `public static Of(...)` factory that validates and throws a domain
exception. Entities are `class : Aggregate<TId>` / `Entity<TId>` (from `BuildingBlocks.Core.DomainModel`).

---

## 3. Data — repository

One repository per aggregate under `Modules/<Plural>/Data/` (interface + `Dynamo*Repository`).
Reference: `Modules/Orders/Data/DynamoOrderRepository.cs`. Talks to `IAmazonDynamoDB` directly.
Registered **Scoped** in ServiceRegistration.

---

## 4. Startup + ServiceRegistration (single DI composition)

`Startup.cs` (root namespace) is `[LambdaStartup]` and **must register `IConfiguration`** so
`[LambdaFunction]` methods can inject it via `[FromServices]`:
```csharp
[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        services.AddSingleton<IConfiguration>(configuration);   // needed for [FromServices]
        services.Add<Service>Services(configuration);
    }
}
```

`Shared/Configuration/ServiceRegistration.cs` is the ONE composition used by every Lambda +
the seeder. Reference: `Ordering.Function/Shared/Configuration/ServiceRegistration.cs`. Registers
MediatR + `ValidationBehavior`/`LoggingBehavior` + FluentValidation, `IAmazonDynamoDB`, the repo
(Scoped), `AddEventBridgeMessaging`, `AddIdempotentEventConsumer`, and the publisher rules +
`StreamRuleDispatcher` (Scoped).

---

## 5. Classify each operation — direct DynamoDB resolver vs Lambda (ADR-0009)

**Default = AppSync direct DynamoDB resolver (NO Lambda).** Only keep a Lambda when the operation
needs real logic (see `resolver-selection` skill / ADR-0009). Simple key/GSI reads, DeleteItem,
PutItem/UpdateItem → direct resolver.

To make an operation a **direct resolver** (like Ordering's `ordersByCustomer`/`deleteOrder`):
1. Write a JS resolver file `src/WebApps/Shopping.Web.SPA.React/graphql/resolvers/<Type>.<field>.js`.
   References: `Query.ordersByCustomer.js` (GSI query), `Mutation.deleteOrder.js` (admin DeleteItem).
   ```js
   import { util } from '@aws-appsync/utils'
   export function request(ctx) {
     const req = { operation: 'Query', index: 'GSI1',
       query: { expression: 'GSI1PK = :pk',
                expressionValues: util.dynamodb.toMapValues({ ':pk': `CUSTOMER#${ctx.identity.sub}` }) },
       scanIndexForward: false }
     if (ctx.args.nextToken) req.nextToken = ctx.args.nextToken
     return req
   }
   export function response(ctx) {
     if (ctx.error) util.error(ctx.error.message, ctx.error.type)
     return { items: (ctx.result.items ?? []).map(i => ({ /* map raw attrs -> schema */ })),
              nextToken: ctx.result.nextToken ?? null }
   }
   ```
2. Wire it in `infra/constructs/appsync-api.ts` on the DynamoDB data source (not a Lambda DS):
   `this.resolver(<table>Ds, '<Name>Resolver', 'Query'|'Mutation', '<field>');`
   Grant `grantReadWriteData` if it writes/deletes.
3. Update local dev resolver `src/WebApps/Shopping.Web.SPA.React/app/api/graphql/local.ts` to hit
   DynamoDB Local directly (AWS SDK `QueryCommand`/`DeleteItemCommand`) — mirror the `orders`/
   `ordersByName` resolvers there.
4. Delete the Lambda feature + its AppHost/CDK registration + launchSettings profile + any now-dead
   repository method + its unit test.

Admin-only resolvers check `ctx.identity.groups` and call `util.unauthorized()`. Never trust a
client-supplied owner id — derive it from `ctx.identity.sub`.

**Feature slice** (when a Lambda IS warranted): folder per action with generic file names —
`Endpoint.cs` (the `[LambdaFunction]` on `Functions`), `Handler.cs` (MediatR), optional
`Validator.cs`/`Mapper.cs`. Single-use mappers → private method inside the Handler, no extra file.

---

## 6. EventsIntegration — Consumers

Inbound integration events. Same shape as a Feature: `Consumers/<Event>/{Endpoint, Handler, Mapper, Command}`.
Idempotent via `IIdempotentEventConsumer` (the inbox write + the business write happen in ONE
`TransactWriteItems`, keyed by `evt.Id`). Reference: `Consumers/BasketCheckout/`.

---

## 7. EventsIntegration — Publishers (rule-based, ADR-0019)

**When:** use the rule pattern only if the publisher emits **2+ event types / reacts to a state
transition**. A single unconditional publish (publish on every insert) can stay a plain handler.

Shared abstractions (`src/BuildingBlocks/BuildingBlocks.Messaging/Streams/*` + `EventBridge/PublishInstruction.cs`):
- `StreamContext<TImage>(EventName, Old, New)` — old + new images (needs stream `NEW_AND_OLD_IMAGES`).
- `IStreamRule<TImage>` — `bool Match(ctx)` + `Task<PublishInstruction> BuildAsync(ctx, ct)`.
- `PublishInstruction(string DetailType, object Payload)` — transport-agnostic.
- `StreamRuleDispatcher<TImage>` — runs matching rules, calls `IEventPublisher.PublishAsync(instruction)`.

**RULE: a rule holds only domain logic. It returns a `PublishInstruction` and NEVER references
`IAmazonEventBridge`/`PutEventsRequest`.** `IEventPublisher` is the only EventBridge-aware component.

**Integration event types live in `BuildingBlocks.Messaging.Events`** (a published event is a
cross-service contract, so it belongs in the shared library — like `BasketCheckoutEvent`). Use
**flat primitives** (no service DTOs; enums as `int`), because BuildingBlocks can't reference a
service. Rules live in `Modules/<Plural>/EventsIntegration/Publishers/Rules/<X>Rule.cs`, plus a
snapshot image at `Publishers/<Agg>StreamImage.cs`.
References: `src/BuildingBlocks/BuildingBlocks.Messaging/Events/OrderCreatedEvent.cs`,
`Publishers/Rules/OrderCreatedRule.cs`, `Publishers/OrderStreamImage.cs`.
```csharp
// Rule — Publishers/Rules/<X>Rule.cs  (the <Event> type lives in BuildingBlocks.Messaging.Events)
public sealed class <X>Rule(I<Agg>Repository repo) : IStreamRule<<Agg>StreamImage>
{
    public bool Match(StreamContext<<Agg>StreamImage> ctx) =>
        ctx.EventName == "INSERT" && ctx.New?.Type == "<Type>";
        // transition: ctx.Old?.Status != "Approved" && ctx.New?.Status == "Approved"

    public async Task<PublishInstruction> BuildAsync(StreamContext<<Agg>StreamImage> ctx, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(ctx.New!.Id, ct);
        return new PublishInstruction(nameof(<Event>), entity!.To<Event>());
    }
}
```
Register each rule + the dispatcher **Scoped** in ServiceRegistration:
```csharp
services.AddScoped<IStreamRule<<Agg>StreamImage>, <X>Rule>();
services.AddScoped<StreamRuleDispatcher<<Agg>StreamImage>>();
```
Set the source table's stream to `NEW_AND_OLD_IMAGES` (dev seeder `DynamoTableInitializer` + CDK
`*-dynamodb.ts`) so transition rules see the old image.

---

## 8. Event Lambdas = `[LambdaFunction]` methods (no manual DI bootstrap)

Consumer and stream publisher are `[LambdaFunction]` methods on `partial class Functions` in the
**root** namespace `<Service>.Function`. DI + per-invocation scope + input deserialization come
from `[LambdaStartup] Startup`; inject services with `[FromServices]`. Do **NOT** hand-build a
`ServiceCollection`/`BuildServiceProvider` in a constructor.

References: `Consumers/BasketCheckout/Endpoint.cs`, `Publishers/OrderStreamPublisherFunction.cs`.
```csharp
// Consumer — file under Consumers/<Event>/Endpoint.cs, but namespace = root
using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Idempotency;
using <Service>.Function.Modules.<Plural>.EventsIntegration.Consumers.<Event>;

namespace <Service>.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task <Event>Consumer(
        EventBridgeEvent<<Event>> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = <Event>Mapper.To<X>Command(evt.Detail);
        var entity  = <X>Handler.CreateNew(command);
        await consumer.ConsumeAsync(evt.Id, [Dynamo<Agg>Repository.ToTransactWriteItem(entity)]);
    }
}
```
```csharp
// Stream publisher — file under Publishers/<Agg>StreamPublisherFunction.cs, namespace = root
using Amazon.Lambda.DynamoDBEvents;
using BuildingBlocks.Messaging.Streams;
using Microsoft.Extensions.Configuration;
using <Service>.Function.Modules.<Plural>.EventsIntegration.Publishers;

namespace <Service>.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task <Agg>StreamPublisher(
        DynamoDBEvent dynamoEvent,
        [FromServices] StreamRuleDispatcher<<Agg>StreamImage> dispatcher)
    {
        foreach (var record in dynamoEvent.Records)
        {
            var context = new StreamContext<<Agg>StreamImage>(
                record.EventName,
                <Agg>StreamImage.From(record.Dynamodb.OldImage),
                <Agg>StreamImage.From(record.Dynamodb.NewImage));
            await dispatcher.DispatchAsync(context);
        }
    }
}
```
(Feature-gate reads: add `[FromServices] IConfiguration configuration` and early-return.)
Rules/dispatcher are Scoped and resolve inside the generator's per-invocation scope — a Scoped
rule may depend on the Scoped repository without a captive-dependency error.

---

## 9. Handler strings (NOT compile-checked — always verify)

An Annotations `[LambdaFunction]` method `Xyz` generates the handler:
`<Assembly>::<Assembly>.Functions_Xyz_Generated::Xyz`
(e.g. `Ordering.Function::Ordering.Function.Functions_OrderStreamPublisher_Generated::OrderStreamPublisher`).

Update the SAME string in all three places whenever a method is added/renamed/moved:
1. `src/AppHost/<Service>Extensions.cs` (`lambdaHandler:` in `AddAWSLambdaFunction<...>`).
2. `infra/constructs/<service>-lambdas.ts` (`orderingCode([ '...' ])`).
3. `src/Services/<Service>/<Service>.Function/Properties/launchSettings.json`.

Verify the generated names (handler strings are string literals — a typo fails only at runtime):
```bash
rm -rf src/Services/<Service>/<Service>.Function/obj/Debug/net10.0/generated
dotnet build <Service>.Function.csproj --no-incremental -p:EmitCompilerGeneratedFiles=true
ls src/Services/<Service>/<Service>.Function/obj/Debug/net10.0/generated/Amazon.Lambda.Annotations.SourceGenerator/*/*.g.cs
```

---

## 10. What to use from BuildingBlocks

- **Core:** `Aggregate<TId>`/`Entity<TId>`, `ValueObject`/`ValueObject<T>`, `ICommand<T>`/`ICommandHandler<,>`, `BadRequestException`.
- **Messaging:** `IntegrationEvent` (+ define each published event here as a flat-primitive record, e.g. `OrderCreatedEvent`), `EventBridgeEvent<T>`, `IEventPublisher` + `PublishInstruction`, `IIdempotentEventConsumer` + `AddIdempotentEventConsumer`, `AddEventBridgeMessaging`, `IStreamRule<T>`/`StreamRuleDispatcher<T>`/`StreamContext<T>`.
- **ServiceDefaults:** `AddServiceDefaults` (seeder), `ValidationBehavior`, `LoggingBehavior`.

---

## 11. Functional tests

A fully-migrated service has **no HTTP surface** (queries/mutations are AppSync direct resolvers;
Lambdas are event-driven). So functional tests boot the Aspire graph and assert the **DynamoDB
data layer** (the operations the resolvers rely on), not HTTP endpoints. Reference:
`tests/Services/Ordering/Ordering.FunctionalTests/` (adds `AWSSDK.DynamoDBv2`, connects to
DynamoDB Local at the AppHost's fixed port, polls until the seeder ran, asserts GSI query +
DeleteItem).

---

## Gotchas (each with the error it causes)

- **Singular module name** → `CS0118: 'X' is a namespace but is used like a type`. Use plural.
- **`Functions` partial in a non-root namespace** → a partial class must be one namespace; split
  it and the generated handler name changes. Keep every `[LambdaFunction]` in `<Service>.Function`.
- **Handler string typo** → not a compile error; the Lambda fails at runtime. Always verify (§9).
- **Rule references EventBridge** → couples domain to AWS. Rules return `PublishInstruction` only.
- **Stream is `NEW_IMAGE`** → transition rules can't see `ctx.Old`. Use `NEW_AND_OLD_IMAGES`.
- **`[FromServices] IConfiguration` unresolved** → register `IConfiguration` in `Startup` (§4).
- **Singleton rule/dispatcher depending on Scoped repo** → captive dependency. Register Scoped.
- **Deleting a Lambda feature but leaving its AppSync resolver on a Lambda data source** → the
  resolver targets a deleted function. Repoint the resolver to the DynamoDB data source (§5).

## Verify (per service)
```bash
dotnet build tests/Services/<Service>/<Service>.UnitTests/<Service>.UnitTests.csproj   # builds the Function too
dotnet test  tests/Services/<Service>/<Service>.UnitTests/<Service>.UnitTests.csproj
dotnet build src/AppHost/AppHost.csproj                                                # handler strings compile in context
dotnet format src/Services/<Service>/<Service>.Function/<Service>.Function.csproj
```
