/// <reference path="./.sst/platform/config.d.ts" />

/**
 * Deploys the SPA to AWS via OpenNext/SST — replaces the hand-rolled CDK
 * stack (infra/stacks/spa-stack.ts and friends, removed) that had to
 * reverse-engineer OpenNext's bundled tag-cache/CloudFront wiring by hand.
 * See docs/adr/0020-migrate-spa-deploy-to-sst.md.
 *
 * On-demand ISR revalidation (ProductCreatedEvent/ProductUpdatedEvent/ProductDeletedEvent/
 * ReviewCreatedEvent/ReviewUpdatedEvent ->
 * revalidateTag() -> invalidate the affected CloudFront path) is still a
 * custom Lambda (revalidator/index.mjs) subscribed to the existing
 * `duckstore-event-bus` — SST's Nextjs component only invalidates CloudFront
 * at deploy time, not on business events. The Lambda calls the SPA's single
 * generic, HMAC-signed webhook (app/api/webhooks/revalidate/route.ts) to
 * trigger revalidateTag() — the real Next.js API — rather than writing to
 * the OpenNext DynamoDB tag-cache table directly.
 */
export default $config({
  app(input) {
    return {
      name: "duckstore-spa",
      removal: input?.stage === "production" ? "retain" : "remove",
      protect: ["production"].includes(input?.stage),
      home: "aws",
    };
  },
  async run() {
    // sst.config.ts can't have top-level imports at all (SST refuses to even
    // run `sst secret set`/`sst deploy` otherwise: "Your sst.config.ts has
    // top level imports - this is not allowed") — Node built-ins have to be
    // dynamically imported inside run() instead.
    const { readFileSync } = await import("fs");
    const { join } = await import("path");

    const environmentName = $app.stage;
    const hostedZoneDomainName = "keveenmenezes.com";
    const domainName = `${environmentName}-duckstore.${hostedZoneDomainName}`;

    // The AppSync/Cognito stack stays on CDK — these are its CloudFormation
    // exports (infra/stacks/appsync-stack.ts), read directly instead of via
    // `cdk.Fn.importValue` since this app is no longer a CDK stack.
    const appsyncUrl = aws.cloudformation.getExportOutput({
      name: "DuckStoreAppSyncStack-ApiUrl",
    }).value;
    const appsyncApiKey = aws.cloudformation.getExportOutput({
      name: "DuckStoreAppSyncStack-ApiKey",
    }).value;
    const cognitoClientId = aws.cloudformation.getExportOutput({
      name: "DuckStoreAppSyncStack-UserPoolClientId",
    }).value;
    const cognitoHostedUiUrl = aws.cloudformation.getExportOutput({
      name: "DuckStoreAppSyncStack-HostedUiUrl",
    }).value;

    // Secret for the generic revalidation webhook
    // (app/api/webhooks/revalidate/route.ts) — verifies the HMAC signature
    // on the `revalidator` Lambda's calls below. That route is
    // server-to-server only (no browser ever calls it — see ADR-0020), so
    // this is the only auth path it has.
    const webhookSecret = new sst.Secret("WebhookSecret");

    const nextjs = new sst.aws.Nextjs("Spa", {
      // SST defaults to downloading its own pinned OpenNext version
      // (currently 3.9.14) via `npx open-next@<version> build`, ignoring the
      // `@opennextjs/aws@^4.0.3` devDependency this repo intentionally pins
      // and its `open-next.config.ts` overrides (the sharp binary workaround
      // for the image optimizer — see that file's comment). Explicit
      // `buildCommand` makes it run our own script/version instead.
      buildCommand: "pnpm build:opennext",
      domain: {
        name: domainName,
        dns: sst.aws.dns(),
      },
      // Deploy-time-only invalidation (new build's chunk filenames changing
      // under the same cached HTML shells) — the tag-driven runtime
      // invalidation below is what handles data changes.
      invalidation: {
        paths: "all",
        wait: false,
      },
      environment: {
        GRAPHQL_BACKEND: "appsync",
        APPSYNC_URL: appsyncUrl,
        APPSYNC_API_KEY: appsyncApiKey,
        COGNITO_CLIENT_ID: cognitoClientId,
        COGNITO_HOSTED_UI_URL: cognitoHostedUiUrl,
        WEBHOOK_SECRET: webhookSecret.value,
        NEXT_PUBLIC_SITE_URL: `https://${domainName}`,
      },
      // No OPEN_NEXT_BUILD_ID env var needed here, even though the bundled
      // tag-cache handler prefixes every DynamoDB key with it: OpenNext v4's
      // server adapter self-assigns it at startup from the build-time-baked
      // BuildId (`process.env.OPEN_NEXT_BUILD_ID = NextConfig.deploymentId ??
      // BuildId`, @opennextjs/aws dist/adapters/config/index.js) — confirmed
      // in practice, since revalidateTag() worked on a deployment whose
      // Lambda never had the env var set externally.
    });

    // The CDN distribution SST created — the revalidator invalidates paths
    // on it directly. It no longer touches the tag-cache DynamoDB table at
    // all: that's now Next.js's own job, via revalidateTag() inside the
    // generic revalidate webhook the revalidator calls over HTTP instead.
    const distribution = nextjs.nodes.cdn!.nodes.distribution;

    // Catalog (still CDK) publishes to this bus by fixed name — same lookup
    // infra/constructs/spa-tag-revalidator.ts (removed) used via
    // `events.EventBus.fromEventBusName`, just expressed as an ARN here since
    // `Bus.subscribe` takes an ARN, not a bus name.
    const eventBusArn = $interpolate`arn:aws:events:${aws.getRegionOutput().name}:${aws.getCallerIdentityOutput().accountId}:event-bus/duckstore-event-bus`;

    sst.aws.Bus.subscribe("Revalidator", eventBusArn, {
      handler: "revalidator/index.handler",
      environment: {
        SPA_URL: `https://${domainName}`,
        WEBHOOK_SECRET: webhookSecret.value,
        CLOUDFRONT_DISTRIBUTION_ID: distribution.id,
      },
      permissions: [
        {
          actions: ["cloudfront:CreateInvalidation"],
          resources: [distribution.arn],
        },
      ],
    }, {
      pattern: {
        source: ["duckstore"],
        detailType: [
          "ProductCreatedEvent",
          "ProductUpdatedEvent",
          "ProductDeletedEvent",
          "ReviewCreatedEvent",
          "ReviewUpdatedEvent",
        ],
      },
    });

    return {
      url: nextjs.url,
      distributionId: distribution.id,
    };
  },
});
