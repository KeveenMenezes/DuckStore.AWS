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

## 2. GitHub OIDC provider and deploy role

The workflows authenticate to AWS through OIDC federation — there are no long-lived
access keys. Register the provider once per account:

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com
```

Then create the role the workflows assume, with this trust policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
          "token.actions.githubusercontent.com:sub": "repo:KeveenMenezes/DuckStore.AWS:environment:development"
        }
      }
    }
  ]
}
```

Both conditions matter, and both are easy to get wrong:

- **`aud` must be checked.** Without it the role trusts any token the GitHub OIDC
  provider ever issued, including tokens minted for a different audience entirely.
- **`sub` must be `StringEquals`, never `StringLike` with a wildcard.** A
  `repo:OWNER/REPO:*` pattern lets *any* ref of the repository assume the role —
  every branch, every tag, every pull request. In a public repository that is the
  difference between "the deploy branch can deploy" and "anything that lands a ref
  can deploy".

Pinning `sub` to `environment:development` (rather than a branch ref) works because
every deploy job declares `environment: development`, and that GitHub Environment is
itself restricted to the `development` branch — so the branch restriction is enforced
by GitHub before a token is ever issued.

Publish the role ARN to the workflows as a **secret**, not a variable — Actions
variables are rendered in plaintext in workflow logs, and those logs are world-readable
on a public repository:

```bash
gh secret set AWS_DEPLOY_ROLE_ARN --body 'arn:aws:iam::<ACCOUNT_ID>:role/GitHubActionsDeployRole'
```

## 3. Social federation client secrets (GitHub Environment secrets)

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
