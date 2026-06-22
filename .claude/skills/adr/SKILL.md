---
name: adr
description: Use when the user wants to create, update, or review an Architecture Decision Record (ADR) for DuckStore — e.g. "write an ADR for X", "document this architectural decision", "supersede ADR-000N". Encodes the project's mandatory ADR-0000 standard (sections, status values, numbering) and the style of existing ADRs under docs/adr/.
---

# DuckStore ADR skill

Creates and maintains Architecture Decision Records under `docs/adr/`, following the
governance defined in `docs/adr/0000-official-architecture-decisios-records-standard.md`.

## When to create an ADR

Per ADR-0000, create one for decisions with real architectural impact: technology/platform
selection, service-to-service communication, persistence patterns, observability, security
and authentication, versioning/external integration, infrastructure policy, or structural
domain (DDD/bounded context) changes.

Do **not** create an ADR for trivial implementation details or decisions with no
architectural impact — say so and skip it rather than padding docs/adr with noise.

## Before writing

1. `ls docs/adr/` and read the highest-numbered existing ADR plus any ADR that looks related
   to the topic (e.g. don't propose something that contradicts an Accepted ADR without
   explicitly superseding it).
2. Determine the next sequential number — zero-padded to 4 digits, never reused, even for
   superseded/obsolete records.
3. Pick a short kebab-case slug for the filename: `NNNN-short-decision-name.md`.

## Required structure (mandatory per ADR-0000)

Every ADR file must contain exactly these sections, in this order:

1. `# ADR-NNNN: Objective Topic` (title)
2. `## Status` — one of `Proposed`, `Accepted`, `Superseded`, `Obsolete`, with month/year,
   e.g. `**Proposed** — June 2026`. If superseded, explicitly link to the replacing ADR.
3. `## Context` — the problem, constraints, current scenario. Bullet the concrete pains
   that motivate the decision (existing ADRs do this well — see ADR-0001/0002).
4. `## Decision` — the chosen approach and its boundaries. Use subheadings for distinct
   rules (existing ADRs number them, e.g. "Single Client Per API"), and include concrete
   code/config/JSON snippets and a Correct/Incorrect pair when the decision constrains how
   code gets written (see ADR-0001's Implementation Pattern section). Use a Mermaid sequence
   diagram if the decision involves a multi-step flow across services (see ADR-0003).
5. `## Consequences` — split into `### Positive` and `### Negative` (or `### Negative / Costs`)
   bullet lists. Add a `### Mitigation Strategies` subsection if there are real negatives.
6. `## References` (or `## Related Documentation` immediately before it) — link related
   ADRs by relative path, e.g. `[ADR-0002: ...](./0002-....md)`, plus any RFCs/diagrams/PRs.

Optional but encouraged when relevant (precedent in existing ADRs): an `## Applies To` list
of the specific services/projects under `src/Services/*` or `src/*` affected, and a
`### Future Constraints` subsection under Consequences when the decision restricts later work.

## Style notes from existing ADRs

- Headings use `##`/`###`; sections are separated by `---` horizontal rules in some ADRs
  (0000, 0003) but not others (0001, 0002) — either is fine, stay consistent within one file.
- Tables are used for status-value legends, environment-specific policy variants, and
  responsibility breakdowns — prefer a table over prose when comparing 3+ similar options.
- Write rules as imperative MUST/MAY/NOT ALLOWED statements when they are binding
  constraints on how services are built, not just descriptive prose.
- Keep filenames matching the H1 title's slug.

## After drafting

- Remind the user that ADR-0000 requires the ADR to be reviewed via Pull Request with at
  least one technical reviewer approval before the decision is implemented, and that the
  Status must be kept in sync if the decision later changes.
- If this ADR supersedes an existing one, edit that ADR's `## Status` to `**Superseded** —
  <month year>` with a link to the new ADR — do not delete or silently rewrite it (ADR-0000
  explicitly forbids deleting ADRs).
- If asked to update the official template itself, that lives inside ADR-0000 — changing it
  is itself an architectural governance decision and should go through the same PR review.