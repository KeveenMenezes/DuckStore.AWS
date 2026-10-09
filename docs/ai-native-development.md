<sub>[← Back to README](../README.md) · [Architecture](./architecture.md) · [AI-native development](./ai-native-development.md) · [Developer guide](./developer-guide.md)</sub>

# DuckStore — Built to be developed with AI

DuckStore is written on the assumption that **a coding agent is a first-class contributor**, and
that agent is primarily **[Claude](https://claude.com/claude-code)**. That isn't a badge — it's a
set of files checked into the repository whose only job is to make an AI collaborator produce code
that looks like the rest of the codebase.

The bet behind it: an LLM is excellent at *applying* a convention and terrible at *inventing a
consistent one across 10 bounded contexts*. So the conventions are written down, close to the code,
in a form an agent reads before acting.

### What changes when the architect designs *for* the AI

The interesting claim isn't "AI writes the code faster". It's that **an architect who understands
how Claude consumes a repository will choose a different architecture** — and that architecture
happens to be a better one on the axes teams actually get measured on:

| | Traditional layered service | DuckStore, designed for AI collaboration |
|---|---|---|
| **Boundaries** | A shared database and a "we'll split it later" monolith, because splitting is expensive in people-hours. | Ten bounded contexts, each owning its data. The cost of maintaining them is mostly repetition — which is exactly what an agent absorbs. |
| **Development flow** | Onboarding means asking a senior why a class exists. | The *why* is in 47+ ADRs and per-context READMEs; an agent (or a new hire) reads it before touching anything. |
| **Consistency** | Enforced by code review, i.e. by whoever is awake. | Encoded as [skills](../.claude/skills) an agent executes, and checked by the [`adr-guardian`](../.claude/agents) subagent before the PR. |
| **Security** | Coarse roles, because per-endpoint IAM is tedious. | One Lambda per use case ⇒ per-use-case least-privilege grants — tedium is no longer the constraint. |
| **Observability** | Added after the first production incident. | `AddLambdaDefaults()` on every function from day one: X-Ray traces, structured logs, DLQ alarms. |
| **Performance** | Optimized late, once a profiler says so. | Native AOT, arm64, direct DynamoDB resolvers and denormalized read models are the *default* path, not a later project. |
| **Documentation** | Drifts, then gets deleted. | Diagrams are validated against the code by [a script](../scripts/validate-diagrams.py); drift fails loudly. |

The pattern repeats: **the practices that make an architecture good are the same practices that
make it legible to an AI collaborator** — explicit boundaries, one job per unit, stable naming,
written rationale, automated verification. Design for one and you get the other. That's why the
architect's job here got *more* valuable, not less: the model applies conventions relentlessly, but
someone still has to decide that Pricing owns discounts and Basket owns none of it.

### The context layer

| File / folder | Role |
|---|---|
| [`CLAUDE.md`](../CLAUDE.md) | The project brief every session starts from: architecture, conventions, commands, service-by-service map. |
| [`AGENTS.md`](../AGENTS.md) | A **symlink to `CLAUDE.md`** — Codex and other agent tooling read the same single source, so the guidance can never fork. |
| [`docs/adr/`](./adr) | 47+ ADRs — the *why* that source code structurally cannot carry, including why removed things were removed. |
| [`src/Services/*/README.md`](../src/Services) | One README per bounded context: diagram, data model, AppSync surface, events, failure handling. |

### Skills — conventions an agent can execute

[`.claude/skills/`](../.claude/skills) (mirrored under `.agents/skills/` for other agent runtimes)
holds procedural rules that would otherwise live only in the author's head:

| Skill | Encodes |
|---|---|
| `adr` | The mandatory ADR-0000 format, numbering and status lifecycle. |
| `service-architecture` | The Ordering reference shape — module-per-aggregate, rule-based stream publishers. |
| `resolver-selection` | ADR-0009's rule: direct DynamoDB by default, Lambda only as a justified escalation. |
| `thin-handlers-rich-domain` | Handlers orchestrate; business rules belong in entities and domain services. |
| `rendering-strategy` | SSR / SSG / ISR choice for Next.js pages — ISR invalidated by events, never TTL. |
| `architecture-diagrams` | How to edit the `.drawio` source, re-export SVGs and keep the READMEs in sync. |
| `comments` | Comment only non-obvious business rules; every Lambda gets a summary header; English only. |
| `readme-docs` | How a dictated note becomes a properly placed README entry (this section included). |

### Subagents — repeatable jobs

[`.claude/agents/`](../.claude/agents) (with TOML equivalents in [`.codex/agents/`](../.codex/agents)):

- **`adr-guardian`** — reviews a diff against the Accepted ADRs and flags violations, including the
  resurrection of patterns an ADR already superseded. Effectively an architectural linter.
- **`cdc-integration-scaffold`** — generates a new EventsIntegration consumer/publisher from the
  reference shape, plus the CDK rule/DLQ wiring checklist.
- **`appsync-resolver-scaffold`** — writes a classified field's resolver and wires it into
  `schema.graphql` and `appsync-api.ts`.
- **`handler-test-backfill`** — writes the missing xUnit/Moq test for a handler and runs it.

### Guardrails that keep generated code honest

Written context guides an agent; automated checks verify it. Three run without anyone remembering
to ask:

- [`.githooks/pre-commit`](../.githooks) runs `dotnet format` on staged `.cs` files and re-stages
  them — style is never a review topic. Don't bypass it with `--no-verify`.
- [`scripts/validate-diagrams.py`](../scripts/validate-diagrams.py) checks the architecture diagrams
  against the actual code (Lambda names, EventBridge rules, tables), so documentation drift fails
  loudly instead of silently.
- Unit tests per service ([ADR-0024](./adr/0024-testing-strategy-minimum-coverage.md)) plus
  Aspire-driven functional tests for Ordering.

**Working on this repo with Claude Code?** `cd` into the project and run `claude` — `CLAUDE.md`,
the skills and the subagents load automatically. Ask for an ADR before a cross-cutting change, and
run the `adr-guardian` agent before opening a PR.
