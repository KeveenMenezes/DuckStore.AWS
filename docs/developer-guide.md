<sub>[← Back to README](../README.md) · [Architecture](./architecture.md) · [AI-native development](./ai-native-development.md) · [Developer guide](./developer-guide.md)</sub>

# DuckStore — Developer guide

## Repository layout

```
src/
  AppHost/                    .NET Aspire composition root (local orchestration only)
  BuildingBlocks/             Shared cross-cutting code
  Services/<Context>/         One folder per bounded context (+ its README)
  WebApps/                    React SPA (storefront) and Blazor WASM (admin)
infra/                        AWS CDK v2 app — one stack per service
graphql/                      schema.graphql + AppSync JS resolvers
docs/
  adr/                        Architecture Decision Records
  diagrams/                   Exported SVGs (main + one per bounded context)
  duckstore-process-flow.drawio
tests/                        xUnit unit tests per service + functional tests
scripts/                      Diagram export & validation helpers
CLAUDE.md (= AGENTS.md)       Agent context: architecture, conventions, commands
.claude/                      Claude Code skills + subagents (.agents/, .codex/ mirror them)
```

## Prerequisites

- **.NET 10 SDK**
- **Docker** — Aspire runs DynamoDB Local, the Lambda service emulator, and Elasticsearch/Kibana as
  containers
- **Node.js + pnpm** — only needed to run the React SPA (`src/WebApps/Shopping.Web.SPA.React`)
- **Go 1.23+** — only needed to run/test the Notification service directly
- Visual Studio Code with the **C# Dev Kit** extension, or Visual Studio / Rider

## Run everything locally

```bash
git clone https://github.com/KeveenMenezes/DuckStore.AWS.git
cd DuckStore.AWS

# Aspire provisions DynamoDB Local, the Lambda emulator, and Elasticsearch/Kibana,
# registers every Lambda function, and wires the dev environment between them.
dotnet run --project src/AppHost/AppHost.csproj
```

Or press `F5` in VS Code / Visual Studio and select the `AppHost` launch profile.

## Other common commands

```bash
# Build the whole solution
dotnet build DuckStore.slnx

# Run all .NET tests
dotnet test

# Run a single test project
dotnet test tests/Services/Catalog/Catalog.UnitTests/Catalog.UnitTests.csproj

# Validate CDK infra changes before pushing
cd infra && npx cdk synth <StackName>   # e.g. OrderingStack
```


---

## 💡 Tips & Tools

- **Product image pipeline ([ADR-0034](./adr/0034-product-image-pipeline-presigned-post-sqs-sharp-cloudfront.md))**:
  product images upload straight from the admin browser to S3 (presigned POST), are processed into
  AVIF/WebP/JPEG variants by a Node.js/Sharp Lambda, and are served from a dedicated CloudFront
  distribution. The API only carries image metadata (`imageId`) — clients build URLs from
  configuration:
  - React SPA: `NEXT_PUBLIC_IMAGE_CDN_URL` (in `.env.local` for dev; set by `sst.config.ts` when
    deployed). Local dev also needs `IMAGE_ORIGINALS_BUCKET` plus real AWS credentials for the
    upload mutation, since there is no local S3 — dev/test run against the real AWS dev environment.
  - Blazor management app: `ImageCdn:BaseUrl` in `wwwroot/appsettings*.json`.
- **Orphan image cleanup**: uploads whose product form was abandoned leave unreferenced objects in
  the image buckets. Clean them with the manual sweep script (dry-run by default; add `--delete` to
  actually remove):
  ```bash
  cd src/Services/ProductImages
  npx tsx scripts/sweep-orphan-images.ts --originals <originals-bucket> --processed <processed-bucket>
  ```

---

## 📚 Documentation

Every cross-cutting architectural decision — from the serverless migration itself to session
management, CDC event naming, and Lambda packaging — is recorded under [`docs/adr/`](./adr),
numbered sequentially and never deleted, even when superseded. The
[ADR index](./adr/README.md) lists them all; start with the foundations:

| ADR | Decision |
|---|---|
| [0004](./adr/0004-aws-first-eventbridge-over-masstransit-rabbitmq.md) | EventBridge replaces MassTransit/RabbitMQ |
| [0005](./adr/0005-remove-domain-events-cdc-via-dynamodb-streams.md) | No in-process domain events — integration events come from DynamoDB Streams (CDC) |
| [0007](./adr/0007-appsync-graphql-with-direct-dynamodb-resolvers.md) · [0009](./adr/0009-appsync-resolver-selection-direct-first-lambda-for-complex-logic.md) | AppSync with direct DynamoDB resolvers; Lambda only as an escalation |
| [0019](./adr/0019-module-oriented-service-structure-and-rule-based-stream-publishers.md) | Module-oriented service structure + rule-based stream publishers |
| [0026](./adr/0026-pricing-bounded-context-price-and-campaign-ownership.md) | Pricing owns price and campaigns; Basket owns none of it |
| [0027](./adr/0027-catalogview-opensearch-product-search-and-rating-sync.md) · [0030](./adr/0030-catalogview-dynamodb-drop-opensearch.md) | CatalogView as the read side, on DynamoDB |

> The codebase is mid-evolution — it was migrated from PostgreSQL/Marten, EF Core, RabbitMQ, gRPC
> and Carter. If something looks like it *should* be there and isn't, an ADR probably explains why
> it was removed.

Diagrams are authored in
[`docs/duckstore-process-flow.drawio`](./duckstore-process-flow.drawio) — one page per bounded
context, plus the system overview, the request flow and the purchase journey — exported to
`docs/diagrams/*.svg` with
[`scripts/export-diagrams.sh`](../scripts/export-diagrams.sh) and checked against the code by
[`scripts/validate-diagrams.py`](../scripts/validate-diagrams.py). The SVGs are generated artefacts —
re-export after editing the `.drawio`, since a stale diagram is worse than none.

See also: [Contributing guide](../CONTRIBUTING.md) · [Code of conduct](../CODE_OF_CONDUCT.md) ·
[Security policy](../SECURITY.md) · [First-time AWS account setup](./one-time-account-setup.md) ·
[Agent context (`CLAUDE.md`)](../CLAUDE.md)
