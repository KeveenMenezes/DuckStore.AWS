---
name: readme-docs
description: Use when the user wants to add or update documentation in the project README based on tips/notes they give in chat — e.g. "document that to access DynamoDB visually you use NoSQL Workbench", "note this tip in the readme", "add this to the docs". The user dictates informal notes/tips in conversation and this skill turns them into well-formatted README entries (in English).
---

# DuckStore README docs skill

Turns informal notes and tips the user gives in chat into clean, well-placed documentation
inside `README.md`. The user keeps commenting things (tools, gotchas, setup steps, useful
links) and this skill writes them up — the user is dictating, not formatting.

## Language and tone

- **Write in English** — the entire README is in English. Match it.
- Keep the existing tone: clear, direct, didactic (this is a learning/demo project).
- Don't invent details the user didn't give. If a tip needs a command, URL, or version the
  user didn't provide, write what you know and ask for the missing piece rather than guessing.

## Before writing

1. Read `README.md` to see current sections and style.
2. Decide where the tip belongs — extend an existing section before creating a new one:
   - Tool/framework mention → `## 🛠️ Technologies Used` (bullet: `**Name**: description`).
   - Setup/prerequisite/how-to-run step → `## 🚀 Getting Started`.
   - A usage tip, gotcha, or "to see X use tool Y" → a dedicated
     `## 💡 Tips & Tools` section. Create it once (right before `## 📧 Contact`) and
     append to it for later tips.

## Style conventions (match the existing README)

- Section headers use an emoji prefix, e.g. `## 💡 Tips & Tools`.
- Sections are separated by `---` horizontal rules (the README uses them between every section).
- Tech/tool bullets follow `- **Name**: short description.` with a trailing period.
- Use fenced code blocks (```bash```) for commands, exactly as in "Steps to Run".
- Use real markdown links `[text](url)` for tools/sites (e.g. the NoSQL Workbench download page).

## Example

User says: *"to get visual access to DynamoDB use NoSQL Workbench"*

Add under `## 💡 Tips & Tools`:

```markdown
- **Visual access to DynamoDB**: to inspect DynamoDB tables (running via LocalStack
  in Aspire) use [NoSQL Workbench](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/workbench.html),
  AWS's official GUI client. Point it at the LocalStack endpoint.
```

## After writing

- Don't restructure or rewrite unrelated parts of the README — only add/adjust what the tip covers.
- If a tip contradicts something already documented, point it out instead of silently overwriting.
- Keep edits small and append-friendly so the user can keep dictating tips one at a time.