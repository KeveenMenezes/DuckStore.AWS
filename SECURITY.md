# Security Policy

## Scope

DuckStore is a **reference/sample application** built to demonstrate serverless-first
AWS architecture with .NET microservices. It is not a production system and carries no
uptime or support guarantees.

Reports are still welcome — insecure patterns in a sample get copied into real systems,
so hardening this repository has real value.

### In scope

- Source code in `src/`, infrastructure code in `infra/`, and GraphQL schemas in `graphql/`
- CI/CD workflow configuration in `.github/workflows/`
- Insecure defaults, IAM over-permissioning, or authentication/authorization flaws
  that a reader would plausibly copy into their own project

### Out of scope

- The `*.keveenmenezes.com` development deployments. These are ephemeral demo
  environments holding only seeded data. **Do not run automated scanners, load
  tests, or intrusive testing against them.**
- Findings that require compromising a maintainer's account or infrastructure
- Missing hardening on components explicitly marked as local-only
  (LocalStack, development data seeders)

## Reporting a Vulnerability

**Do not open a public issue for security problems.**

Report privately through GitHub's Private Vulnerability Reporting:

**[Report a vulnerability](https://github.com/KeveenMenezes/DuckStore.AWS/security/advisories/new)**

Alternatively, open a report from the repository's **Security** tab →
**Advisories** → **Report a vulnerability**.

Please include:

- A description of the vulnerability and the affected component
- Steps to reproduce, or a proof of concept
- The potential impact
- Any suggested remediation

### What to expect

| Stage                | Target                   |
|----------------------|--------------------------|
| Acknowledgement      | Within 7 days            |
| Initial assessment   | Within 14 days           |
| Fix or mitigation    | Depends on severity      |

This is a personal project maintained in spare time, so these are good-faith
targets rather than a contractual SLA. Reporters are credited in the resulting
security advisory unless they ask otherwise.

## Supported Versions

Only the `development` branch (the latest state of the project) receives security
fixes. There are no maintained release branches.

## Secrets and Credentials

This repository holds no secrets. All credentials are supplied at deploy time:

- AWS access uses **OIDC federation** — no long-lived access keys exist
- Identity-provider client secrets are stored as GitHub Actions secrets and
  passed to CloudFormation as `NoEcho` parameters (see
  [ADR 0036](./docs/adr/0036-federation-client-secrets-noecho-parameters-github-secrets.md))
- OAuth **client IDs** in `infra/cdk.json` are public identifiers by design, not secrets

If you believe a credential has been committed, report it privately using the
process above rather than opening an issue.
