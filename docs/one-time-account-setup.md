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

## 2. GitHub OIDC provider and deploy roles

The workflows authenticate to AWS through OIDC federation — there are no long-lived
access keys. Register the provider once per account:

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com
```

There are **two** deploy roles, because the two deploy toolchains have opposite
permission shapes:

| Role | Used by | Needs |
|---|---|---|
| `GitHubActionsDeployRole` | the 12 `deploy-*-cdk.yml` workflows | almost nothing — `cdk deploy` does its real work through the bootstrap roles |
| `GitHubActionsSpaDeployRole` | `deploy-spa-sst.yml` | direct access to every service the SPA is built from |

Splitting them is the point: SST has no equivalent of CDK's bootstrap indirection, so
its role is unavoidably broad. Keeping it separate is what stops the twelve CDK
workflows — the ones that run on almost every push — from inheriting that breadth.

Both roles share the same trust policy:

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

Publish both role ARNs to the workflows as **secrets**, not variables — Actions
variables are rendered in plaintext in workflow logs, and those logs are world-readable
on a public repository:

```bash
gh secret set AWS_DEPLOY_ROLE_ARN     --body 'arn:aws:iam::<ACCOUNT_ID>:role/GitHubActionsDeployRole'
gh secret set AWS_SPA_DEPLOY_ROLE_ARN --body 'arn:aws:iam::<ACCOUNT_ID>:role/GitHubActionsSpaDeployRole'
```

### Permissions — `GitHubActionsDeployRole` (CDK workflows)

Because the account is bootstrapped (§1), `cdk deploy` does not create anything with the
caller's own credentials. It assumes a bootstrap role for each phase — file publishing
for assets, deploy for the CloudFormation calls, lookup for `fromLookup` context — and
CloudFormation itself assumes `cdk-hnb659fds-cfn-exec-role-*` to touch the actual
resources. So the caller only needs to be able to *reach* those roles:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AssumeCdkBootstrapRoles",
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": [
        "arn:aws:iam::<ACCOUNT_ID>:role/cdk-hnb659fds-deploy-role-<ACCOUNT_ID>-us-east-1",
        "arn:aws:iam::<ACCOUNT_ID>:role/cdk-hnb659fds-file-publishing-role-<ACCOUNT_ID>-us-east-1",
        "arn:aws:iam::<ACCOUNT_ID>:role/cdk-hnb659fds-image-publishing-role-<ACCOUNT_ID>-us-east-1",
        "arn:aws:iam::<ACCOUNT_ID>:role/cdk-hnb659fds-lookup-role-<ACCOUNT_ID>-us-east-1"
      ]
    },
    {
      "Sid": "ReadAppSyncStackOutputs",
      "Effect": "Allow",
      "Action": "cloudformation:DescribeStacks",
      "Resource": "arn:aws:cloudformation:us-east-1:<ACCOUNT_ID>:stack/DuckStoreAppSyncStack/*"
    }
  ]
}
```

Four details that are easy to get wrong here:

- **`cdk-hnb659fds-cfn-exec-role-*` is deliberately absent**, and the resource list is
  written out instead of `cdk-hnb659fds-*` for exactly that reason. That role is the one
  carrying `AdministratorAccess`; it is assumed by the CloudFormation *service*, never by
  the caller. A wildcard would hand the admin back and undo the whole change.
- **`cloudformation:DescribeStacks` is not for the CDK.** It is for the
  `aws cloudformation describe-stacks` step in `deploy-management-cdk.yml` and
  `deploy-spa-sst.yml`, which reads `DuckStoreAppSyncStack`'s outputs with the caller's
  own credentials, outside any CDK role.
- **The lookup role is required, but only for `fromLookup`.** `management-stack.ts` and
  `product-images-stack.ts` call `route53.HostedZone.fromLookup`, which is a real API call
  at synth time — without the role the CDK falls back to the caller's credentials and
  synthesizes a dummy zone id. The `fromEventBusName`/`fromFunctionName`/`fromTableName`
  imports that the stacks use everywhere else need **no** permission at all: they only
  build an ARN from a fixed name locally, which is precisely why they were chosen over
  `Fn.importValue`.
- **`sts:GetCallerIdentity` needs no statement.** It is implicitly allowed for every
  principal, which is how `CDK_DEFAULT_ACCOUNT` resolves in `infra/bin/app.ts`.

### Permissions — `GitHubActionsSpaDeployRole` (SST workflow)

`deploy-spa-sst.yml` runs SST v4, which is Pulumi-based and has no bootstrap-role
indirection: it creates the Lambda functions, S3 buckets, CloudFront distribution,
DynamoDB tables, EventBridge rule, Route 53 records and ACM certificate **with the
caller's credentials directly**. The role therefore has to name the services the SPA is
built from, and its floor is genuinely higher than the CDK role's:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "SpaOwnedServices",
      "Effect": "Allow",
      "Action": [
        "acm:*",
        "cloudfront:*",
        "dynamodb:*",
        "events:*",
        "lambda:*",
        "logs:*",
        "route53:*",
        "sqs:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "SstStateAndAssets",
      "Effect": "Allow",
      "Action": "s3:*",
      "Resource": [
        "arn:aws:s3:::sst-asset-*",
        "arn:aws:s3:::sst-asset-*/*",
        "arn:aws:s3:::sst-state-*",
        "arn:aws:s3:::sst-state-*/*",
        "arn:aws:s3:::duckstore-spa-*",
        "arn:aws:s3:::duckstore-spa-*/*"
      ]
    },
    {
      "Sid": "SstBootstrapAndSecrets",
      "Effect": "Allow",
      "Action": "ssm:*",
      "Resource": "arn:aws:ssm:us-east-1:<ACCOUNT_ID>:parameter/sst/*"
    },
    {
      "Sid": "ReadAppSyncStackOutputs",
      "Effect": "Allow",
      "Action": ["cloudformation:DescribeStacks", "cloudformation:ListExports"],
      "Resource": "*"
    },
    {
      "Sid": "ManageSpaExecutionRoles",
      "Effect": "Allow",
      "Action": [
        "iam:CreateRole", "iam:DeleteRole", "iam:GetRole", "iam:TagRole", "iam:UntagRole",
        "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy", "iam:ListRolePolicies",
        "iam:AttachRolePolicy", "iam:DetachRolePolicy", "iam:ListAttachedRolePolicies",
        "iam:UpdateAssumeRolePolicy"
      ],
      "Resource": "*"
    },
    {
      "Sid": "PassExecutionRolesToLambdaOnly",
      "Effect": "Allow",
      "Action": "iam:PassRole",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "iam:PassedToService": ["lambda.amazonaws.com", "edgelambda.amazonaws.com"]
        }
      }
    },
    {
      "Sid": "DenyTouchingDeployIdentities",
      "Effect": "Deny",
      "Action": "*",
      "Resource": [
        "arn:aws:iam::<ACCOUNT_ID>:role/cdk-hnb659fds-*",
        "arn:aws:iam::<ACCOUNT_ID>:role/GitHubActions*"
      ]
    },
    {
      "Sid": "DenyPrivilegeEscalation",
      "Effect": "Deny",
      "Action": [
        "iam:CreateUser", "iam:CreateAccessKey", "iam:CreateLoginProfile",
        "iam:UpdateLoginProfile", "iam:AttachUserPolicy", "iam:PutUserPolicy",
        "iam:CreateGroup", "iam:AttachGroupPolicy", "iam:PutGroupPolicy",
        "iam:CreatePolicyVersion", "iam:SetDefaultPolicyVersion",
        "iam:CreateOpenIDConnectProvider", "iam:UpdateOpenIDConnectProviderThumbprint",
        "iam:CreateSAMLProvider", "iam:UpdateSAMLProvider",
        "organizations:*", "account:*"
      ],
      "Resource": "*"
    }
  ]
}
```

The two `Deny` statements are the part that actually bounds this role, and they are why
it is worth having as a separate identity at all. Without them, `iam:CreateRole` +
`iam:PassRole` + `lambda:CreateFunction` is a complete path back to account admin: create
a role, attach `AdministratorAccess`, hand it to a Lambda, invoke it. Denying the deploy
identities themselves (`cdk-hnb659fds-*`, `GitHubActions*`) closes the shortest version
of that path — the CDK's `cfn-exec-role` is the admin role sitting right there — and
denying the user/group/provider surface closes the rest.

`SstStateAndAssets` needs both `sst-asset-*` and `sst-state-*`, not just the former: the
`/sst/bootstrap` SSM parameter (read by `SstBootstrapAndSecrets`) names two separate
buckets — `asset` for published Lambda code and `state` for SST's own Pulumi-backed state,
including `sst secret set` output. Scoping to `sst-asset-*` alone passes the CDK-style
asset-publish steps but fails `sst secret set` with an opaque "Could not get secrets",
since that command reads/writes the state bucket, not the asset one.

Residual risk worth stating plainly rather than pretending away: this role can still
create a Lambda execution role with broad permissions, and `s3:*`/`dynamodb:*` are
prefix- and account-wide respectively. It is a large reduction from `AdministratorAccess`,
not an airtight boundary. The airtight version is a permissions boundary attached to
every role SST creates, which SST does not do by default.

### Rolling this out

Apply each policy to a **test role first** and run `workflow_dispatch` on each of the 13
workflows against it before repointing the real secrets. A deploy that fails on a missing
permission is cheap; finding the gap after the swap, in the middle of something else, is
not. If one does fail, the error names the denied action and the fix is to add it — the
statements above are grouped by intent so a missing action has an obvious home.

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
