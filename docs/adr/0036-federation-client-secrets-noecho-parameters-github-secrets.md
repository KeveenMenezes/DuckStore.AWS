# ADR-0036: Federation Client Secrets as NoEcho Parameters from GitHub Secrets

## Status
**Accepted** — July 2026

---

## Context

The Cognito social identity providers (Google, Login with Amazon — Shopping pool only, see
[ADR-0017](./0017-user-bounded-context-cognito-idp-only-lazy-provisioning.md)) each need an
OAuth **client secret** at deploy time: `AWS::Cognito::UserPoolIdentityProvider` embeds it
in `ProviderDetails/client_secret`. Client IDs are public and committed in `infra/cdk.json`
context; only the secrets need out-of-band handling. The secrets are deploy-time-only —
no Lambda or runtime component ever reads them.

Three delivery mechanisms were attempted, in order:

1. **Secrets Manager** (original) — `cdk.SecretValue.secretsManager(...)` emitting a
   `{{resolve:secretsmanager:...}}` dynamic reference. Worked everywhere, because
   Secrets Manager references are accepted in **any** resource property and resolve at
   stack *execution* under the admin `cfn-exec` bootstrap role. Cost: $0.40/secret/month
   for secrets that never rotate automatically — the only Secrets Manager feature being
   paid for was the dynamic reference itself.

2. **SSM Parameter Store SecureString** (first replacement attempt) —
   `cdk.SecretValue.ssmSecure(...)`. Free, but failed twice in practice:
   - `{{resolve:ssm-secure:...}}` references are validated at **changeset creation**
     using the *caller's* credentials — the CDK bootstrap deploy role, whose default
     policy only reads `/cdk-bootstrap/*`. The failure surfaces as a misleading
     `Parameters [ssm-secure:...] cannot be found` even when the parameters exist,
     and fixing it required a manual `put-role-policy` on a bootstrap-managed role —
     an undocumented, per-account/region step that would silently be missing in prod.
   - After granting the permission, CloudFormation rejected the reference outright:
     `SSM Secure reference is not supported in
     [AWS::Cognito::UserPoolIdentityProvider/Properties/ProviderDetails/client_secret]`.
     Unlike Secrets Manager references, `ssm-secure` only works on an allowlist of
     resource properties, and Cognito identity providers are not on it. This is a hard
     platform limitation with no workaround on the SSM side.

The comparison that fell out of that experience:

| Mechanism | Cost | Works on Cognito IdP | Extra per-account setup | Secret custody |
|---|---|---|---|---|
| Secrets Manager dynamic ref | $0.40/secret/month | ✅ (resolves at execution) | none | AWS |
| SSM SecureString dynamic ref | free | ❌ rejected by CloudFormation | deploy-role IAM grant | AWS |
| NoEcho parameter + GitHub Secrets | free | ✅ (plain `Ref`) | `gh secret set` per environment | GitHub |

A note on custody: whoever administers the GitHub repository can already modify workflows
and assume `GitHubActionsDeployRole` into the AWS account, so storing these two secrets in
GitHub does not extend the existing trust boundary. And in **all three** mechanisms the
secret's final resting place is Cognito itself — any principal with
`cognito-idp:DescribeIdentityProvider` can read it back. The choice only ever governed the
transport into the template.

---

## Decision

Federation client secrets enter `DuckStoreAppSyncStack` as **NoEcho CloudFormation
parameters**, supplied by the deploy workflow from **GitHub Environment secrets**.

### 1. Stack declares NoEcho parameters

`infra/stacks/appsync-stack.ts` declares one `CfnParameter` per provider and wraps it as
a `SecretValue` for the `AppSyncAuth` construct — the construct's interface is unchanged:

```ts
const googleClientSecret = new cdk.CfnParameter(this, 'GoogleClientSecret', {
  type: 'String',
  noEcho: true,
  default: '',
});
// ...
googleClientSecret: cdk.SecretValue.cfnParameter(googleClientSecret),
```

`NoEcho` masks the value in the CloudFormation console, `DescribeStacks`, and events.
The empty default keeps `cdk synth`/`cdk deploy` working when federation is not
configured (no clientId in context — the provider's button simply isn't rendered).

### 2. Workflow passes them from GitHub Environment secrets

`deploy-appsync-cdk.yml` (no new workflow — the existing deploy step):

```yaml
npx cdk deploy DuckStoreAppSyncStack \
  --parameters GoogleClientSecret=${{ secrets.GOOGLE_FEDERATION_CLIENT_SECRET }} \
  --parameters AmazonClientSecret=${{ secrets.AMAZON_FEDERATION_CLIENT_SECRET }} \
  --require-approval never
```

Secrets are **Environment**-scoped (`development`, later `prod`), matching the
workflow's `environment:` key — each environment carries its own OAuth credentials.

### 3. Binding rules

- Client secrets MUST NOT be committed to the repository, embedded in synthesized
  templates, or stored in paid AWS secret stores for this deploy-time-only use case.
- New federation providers MUST follow the same pattern: public client ID in `cdk.json`
  context, secret as a NoEcho parameter fed from a GitHub Environment secret.
- Onboarding a new environment is `cdk bootstrap` + `gh secret set` per provider —
  documented in `docs/one-time-account-setup.md`. No IAM changes to bootstrap roles are
  required or permitted for secret delivery.
- Rotation is `gh secret set` again followed by re-running the AppSync deploy workflow.

**Correct** — secret flows through a masked parameter:

```bash
gh secret set GOOGLE_FEDERATION_CLIENT_SECRET --env development --body '<secret>'
# workflow: --parameters GoogleClientSecret=${{ secrets.GOOGLE_FEDERATION_CLIENT_SECRET }}
```

**Incorrect** — resurrecting the mechanisms this ADR rejects:

```ts
googleClientSecret: cdk.SecretValue.ssmSecure('/duckstore/federation/google'), // CFN rejects on Cognito IdP
googleClientSecret: cdk.SecretValue.secretsManager('duckstore/federation/google'), // paid, no added value here
```

---

## Applies To

- `infra/stacks/appsync-stack.ts` / `infra/constructs/appsync-auth.ts`
- `.github/workflows/deploy-appsync-cdk.yml`
- `docs/one-time-account-setup.md`

---

## Consequences

### Positive

- **Zero recurring cost** — no Secrets Manager line item; GitHub Secrets and
  CloudFormation parameters are free.
- **No bootstrap-role surgery** — the changeset-time `ssm-secure` IAM gotcha disappears;
  a new account needs only `cdk bootstrap` plus `gh secret set`, both documented.
- **Per-environment by construction** — GitHub Environment scoping maps 1:1 to the
  deploy workflow's `environment:` key, so dev and prod credentials cannot cross.
- **Masked end to end** — NoEcho hides the value in the template, console, and stack
  events; GitHub masks it in workflow logs.

### Negative / Costs

- **Custody moves outside AWS.** Organizations mandating "secrets live in AWS" would
  need to revert to Secrets Manager (the $0.40/secret/month is the price of that
  policy). Acceptable for this project given the repo already holds deploy access.
- **Local deploys need the parameters by hand.** `cdk deploy DuckStoreAppSyncStack`
  without `--parameters` succeeds but provisions identity providers with **empty
  secrets**, silently breaking that provider's sign-in until the next parameterized
  deploy.
- **CloudFormation retains previous parameter values** internally; NoEcho masks but
  does not encrypt them client-side the way SecureString/KMS would.

### Mitigation Strategies

- The empty-default failure mode and the manual `--parameters` invocation are documented
  in `docs/one-time-account-setup.md`; AppSync deploys are expected to run via the
  workflow, where the parameters are always present.
- If a runtime consumer for these secrets ever appears (a Lambda needing the Google
  secret), this ADR must be revisited — GitHub Secrets are unreachable from AWS runtime,
  and that use case shifts the decision toward an AWS-side store.

---

## References

- [ADR-0003: Adoption of Zero Trust Security Model](./0003-adoption-of-zero-trust-security-model.md)
- [ADR-0017: User Bounded Context — Cognito IdP Only, Lazy Provisioning](./0017-user-bounded-context-cognito-idp-only-lazy-provisioning.md)
- [AWS: CloudFormation dynamic references — ssm-secure supported resources](https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/dynamic-references-ssm-secure-strings.html)
- `docs/one-time-account-setup.md` — one-time environment onboarding commands
