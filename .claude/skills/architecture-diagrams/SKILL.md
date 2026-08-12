---
name: architecture-diagrams
description: Use when working with DuckStore's architecture diagrams or the per-bounded-context READMEs — e.g. "update the diagram for X", "the Pricing page is out of date", "add a page for the new service", "regenerate the READMEs", "check the diagrams still match the code". Covers docs/duckstore-backend-improved.drawio, the SVG export, the READMEs under src/Services/*/, and the validator that catches drift.
---

# DuckStore architecture diagrams

`docs/duckstore-backend-improved.drawio` is the single source for every architecture picture in the
repo. One page per bounded context, plus **Main** (system overview) and **Front end**. Each page is
exported to a dark-theme SVG in `docs/diagrams/` and embedded in that context's README under
`src/Services/<Name>/README.md`.

## The two scripts — run these instead of rewriting them

```bash
./scripts/export-diagrams.sh          # .drawio -> docs/diagrams/*.svg (all 12 pages)
python3 scripts/validate-diagrams.py  # checks the diagrams against infra/ and graphql/
python3 scripts/validate-diagrams.py --page Pricing --only content
```

`validate-diagrams.py` is the important one. Every check in it caught a real defect at least once —
do not re-derive these by hand:

| Group | Catches |
|---|---|
| `structure` | Edges with no source/target, semantically impossible edges (table→table), icon overlap, canvas overflow |
| `content` | Lambda/table names that do not exist in `infra/`, wrong stream type or missing TTL, GraphQL fields with no resolver, events missing the `Event` suffix, leftover Portuguese |
| `consistency` | Legend colours out of canonical order or undocumented, icons with no `fillColor`, non-standard Lambda icon shape, font size wrong for its role |
| `docs` | Broken relative links in any README, referenced test projects that do not exist |

**Always run the validator before and after touching the `.drawio`.** Before, so you know which
findings you inherited; after, so you know you did not add any.

## Ground rules

### Facts come from `infra/`, never from the diagram

The `.drawio` is downstream of the code. When writing or checking a page, read
`infra/constructs/<service>-lambdas.ts` and `-dynamodb.ts`, plus `graphql/resolvers/<domain>/`.
A diagram that disagrees with those is the thing that is wrong.

Real drift found this way: `catalogview-price-sync-consumer` (the real name is
`catalogview-**pricing**-sync-consumer`), `product-discounts` labelled "no stream" when ADR-0044
gave it one, and three Challenges mutations named `submitAnswer`/`revealHint`/`redeemPoints` instead
of `submitChallengeAnswer`/`revealChallengeHint`/`redeemChallengePoints`.

### Conventions the validator enforces

- **Event names carry the `Event` suffix** — `PriceChangedEvent`, not `PriceChanged`. It matches the
  CDK `detailType` and the C# type, so the name is greppable.
- **Legend colour order**: sync `#232F3E` → saga `#8C4FFF` → event `#1A7F5A` → image pipeline
  `#2266C4` → failure `#C0392B` → support `#879196`. List only the colours the page actually uses,
  and never use a colour the legend does not document.
- **Group-box font hierarchy**: 13px for the owning stack/context box, 12px for secondary boxes
  (observability, external stacks), 11px for nested module sub-boxes.
- **Icons**: `shape=mxgraph.aws4.resourceIcon;resIcon=...` with an explicit `fillColor`. An icon
  with no `fillColor` renders as a black frame. Lambda is `resIcon=mxgraph.aws4.lambda` + `#ED7100`
  — never `shape=mxgraph.aws4.lambda_function`. Browsers/actors use `shape=mxgraph.aws4.client` +
  `#232F3D`.
- **Circled numbers (①②③) only where the order is genuinely not inferable from the arrows.** They
  belong on Payment (the Payment → Gateway → Payment round trip), Front end (the OpenNext flows) and
  Catalog's saga steps. Do not number a page whose entry points are independent — it invents a
  sequence that does not exist.

### Editing the `.drawio` safely

Edit with a script over `mxCell/@value` and geometry; do not hand-write whole pages unless you are
rebuilding one deliberately.

**The editor is the main hazard.** draw.io keeps the whole file in memory and rewrites it on every
save, so a save after your edit silently reverts your work. It has already, in one session: two
edges lost their endpoints, and the **entire Main page was deleted**. Therefore:

1. Copy the file to a scratch backup before any scripted edit.
2. Tell the user to close or reload draw.io before and after.
3. If a page vanishes, `docs/.$duckstore-backend-improved.drawio.bkp` is the editor's own backup —
   splice the missing `<diagram>` back in rather than restoring the whole file, so other pages keep
   any legitimate edits.
4. After editing, diff geometry against the backup and confirm only what you intended moved.

## Adding a page for a new bounded context

1. Read the service's `infra/constructs/<name>-{lambdas,dynamodb}.ts` and its resolvers.
2. Copy the layout of an existing page of similar shape (Review for CDC-out only, Ordering for
   event-driven, Pricing for a large multi-module context).
3. Run the validator, then `./scripts/export-diagrams.sh`.
4. Write `src/Services/<Name>/README.md` from the template below.
5. Add a linked row to the service table in the root `README.md`.
6. Add a card to the **Main** page — it is a context-level overview, so add the card and its bus
   arrows, not the individual tables and Lambdas.

## README template

`src/Services/<Name>/README.md` — the context directory, **not** `<Name>.Function/` (that level does
not exist for Notification or ProductImages, and would collide with the boilerplate `Readme.md`
already in `Catalog.Function/`).

```markdown
# <Context>

<one sentence: what it owns, and the one thing people get wrong about it>

## Architecture
![<Context> architecture](../../../docs/diagrams/<page>.svg)
<sub>Source: docs/duckstore-backend-improved.drawio, page **<Page>**. Regenerate with `./scripts/export-diagrams.sh`.</sub>

## Responsibilities      — including what it deliberately does NOT do
## Data                  — table | key | stream | notes
## API surface (AppSync) — field | resolver (direct vs Lambda, per ADR-0009)
## Integration events    — Publishes / Consumes, with the real rule names
## Failure handling      — the context DLQ, alarm, SNS topic
## Local development     — Aspire command + this service's test project
## Related ADRs          — relative links into ../../../docs/adr/
```

Write in **English**. State the negative space explicitly — "Basket has zero discount
responsibility", "User has no stream because nothing needs to react to a profile change" — that is
usually the part a reader cannot infer from the code.

## Diagrams are dark-theme only

`export-diagrams.sh` exports with `--theme dark` and **bakes the `#121212` canvas into each file**
(both as a CSS background and as a `<rect>`, because the CSS one is ignored when the SVG is served
through `<img>`). draw.io exports transparent by default, which would put light text on a light page
wherever the README is rendered on a light background. Do not add `-t/--transparent`.

Two CLI details that are easy to get wrong: `-p` is **1-based**, and the page order has changed
before — the script resolves each index by page name for that reason.
