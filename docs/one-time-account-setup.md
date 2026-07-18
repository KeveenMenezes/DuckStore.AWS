# One-time environment setup

Commands that must be run **once per environment** (AWS account/region + the matching
GitHub Environment) before the CI/CD workflows can deploy DuckStore there. They are not
part of any stack or workflow on purpose: they touch account-level plumbing and
out-of-band secrets that the app's own IaC cannot manage. Run them in order when
onboarding a new environment (e.g. prod); none of them needs to be run again afterwards.

All examples assume `us-east-1` and the `development` GitHub Environment; adjust
accordingly.

## 1. CDK bootstrap

Provisions the CDK asset bucket and the `cdk-hnb659fds-*` roles every `cdk deploy` uses.

```bash
npx cdk bootstrap aws://<ACCOUNT_ID>/us-east-1
```

## 2. Social federation client secrets (GitHub Environment secrets)

The Cognito identity providers receive their OAuth client secrets as **NoEcho
CloudFormation parameters**, passed by `deploy-appsync-cdk.yml` from GitHub Environment
secrets (`cdk deploy --parameters`). Client IDs are public and committed in
`infra/cdk.json` context; only the secrets live here. Values come from the Google Cloud
Console (OAuth client) and the Login with Amazon console.

Why not an AWS-side store: CloudFormation rejects `{{resolve:ssm-secure:...}}` dynamic
references on `AWS::Cognito::UserPoolIdentityProvider/ProviderDetails/client_secret`
(ssm-secure only works on an allowlist of properties), and Secrets Manager — the one
reference type accepted everywhere — costs $0.40/secret/month. NoEcho parameters are
free and masked in the template, console, and logs.

```bash
gh secret set GOOGLE_FEDERATION_CLIENT_SECRET --env development --body '<GOOGLE_CLIENT_SECRET>'
gh secret set AMAZON_FEDERATION_CLIENT_SECRET --env development --body '<AMAZON_CLIENT_SECRET>'
```

Rotating a secret later is just re-running `gh secret set` followed by re-running the
AppSync deploy workflow.

Deploying `DuckStoreAppSyncStack` from a local machine requires passing the same
parameters by hand (`--parameters GoogleClientSecret=... --parameters
AmazonClientSecret=...`); omitting them deploys identity providers with empty secrets,
which breaks that provider's sign-in.
