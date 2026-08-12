---
name: comments
description: Use when writing, reviewing, or cleaning up code comments in DuckStore — e.g. "add comments here", "should this have a comment?", "document this lambda", "review the comments in this file". Encodes the project's comment policy: comment only non-obvious business rules or genuinely complex logic, give every Lambda a summary header, and always write comments in English.
---

# DuckStore comments skill

Decides **when** a comment is worth adding and **how** to write it. The default in this
codebase is *no comment* — the code, names, and vertical-slice structure should carry the
intent. A comment earns its place only when it explains something the code cannot.

## Language

- **Always write comments in English**, regardless of the language used in chat. The codebase
  is English; comments must match it.
- If you touch a file that has an existing comment in another language (e.g. Portuguese) and
  you're editing that area anyway, translate it to English. Don't go file-hunting for
  translations unrelated to your change.

## When to add a comment

Add a comment **only** when one of these is true:

1. **A non-obvious business rule.** The *why* behind a decision that a reader can't infer from
   the code — a domain constraint, a regulatory/financial rule, an intentional edge-case
   handling. Explain the rule and its reason, not the mechanics.
2. **Genuinely complex logic.** An algorithm, a tricky concurrency/ordering concern, a
   workaround for an external system's quirk, or a non-trivial reason for *not* doing the
   obvious thing.

If a reader who knows C#/.NET and this project would understand the line at a glance, **do not
comment it.**

## When NOT to add a comment

- Restating what the code already says (`// increments the counter`).
- Narrating obvious control flow, getters/setters, DI registration, or framework boilerplate.
- Commenting out dead code — delete it instead (git has the history).
- Section-divider banners and decorative noise.
- TODOs without context — if it's worth a TODO, say what and why.

## Lambda summary headers (required)

Every Lambda **must** have a short summary comment directly above its handler class
(the `Function`/`Functions` class), written in English. It states:

- **What triggers it** (DynamoDB Stream, EventBridge, SQS, API Gateway, etc.).
- **What it does** in one or two sentences (its responsibility).
- **Any key design note** worth knowing (e.g. it replaces the outbox pattern, it's feature-flag gated).

Keep it to 2–4 lines. This is the one place a summary comment is always expected, even when the
body is simple.

### Example (matching the existing style)

```csharp
// Triggered by the OrderingTable DynamoDB Stream. Replaces the outbox pattern: the Order write
// itself is the source of truth for the event, so no outbox table or special atomicity between
// the Order and its publication is needed. Gated by the OrderFulfillment feature flag.
public class Function
{
    // ...
}
```

## Style

- Use `//` line comments for code; reserve XML doc comments (`///`) for shared public API in
  `BuildingBlocks/*` only if the surrounding code already uses them — don't introduce them.
- Place the comment directly above the code it explains, no blank line between.
- Write full, plain sentences. Explain the *why*, not the *what*.

## When reviewing existing comments

- Flag and remove comments that only restate the code or narrate the obvious.
- Translate any non-English comment you encounter in code you're already editing.
- Check every Lambda has a summary header; add one if missing.
