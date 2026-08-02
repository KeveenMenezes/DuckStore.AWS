/// <reference path="./.sst/platform/config.d.ts" />

/**
 * Deploys the SPA to AWS via OpenNext/SST — replaces the hand-rolled CDK
 * stack (infra/stacks/spa-stack.ts and friends, removed) that had to
 * reverse-engineer OpenNext's bundled tag-cache/CloudFront wiring by hand.
 * See docs/adr/0020-migrate-spa-deploy-to-sst.md.
 *
 * On-demand ISR revalidation (CatalogViewProductSyncedEvent/CatalogViewProductDeletedEvent/
 * ReviewCreatedEvent/ReviewUpdatedEvent -> revalidateTag() -> invalidate the affected CloudFront
 * path) is still a custom Lambda (revalidator/index.mjs) subscribed to the existing
 * `duckstore-event-bus` — SST's Nextjs component only invalidates CloudFront
 * at deploy time, not on business events. The Lambda calls the SPA's single
 * generic, HMAC-signed webhook (app/api/webhooks/revalidate/route.ts) to
 * trigger revalidateTag() — the real Next.js API — rather than writing to
 * the OpenNext DynamoDB tag-cache table directly.
 *
 * The product/price/rating path (products, products:{id} tags) subscribes to CatalogView's own
 * CDC events (ADR-0035), not the upstream Catalog/Pricing events that feed CatalogView —
 * CatalogView is the actual data source for that part of every ISR page, and subscribing to its
 * events (emitted only after its own DynamoDB write commits) rules out the race where CloudFront
 * gets invalidated before catalogview-products reflects the change that triggered it. It also
 * means a price/rating/category change is covered automatically, without listing every upstream
 * event that happens to touch CatalogView. The reviews:{id} tag (the raw review list, which lives
 * in Review's own store, not catalogview-products) still subscribes directly to
 * ReviewCreatedEvent/ReviewUpdatedEvent — there is no CatalogView event for that data, and no race
 * to fix on that path.
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
    // Delegated subdomain zone of keveenmenezes.com — the zone encodes the environment, so
    // the labels below don't repeat it. Must stay in lockstep with infra/bin/app.ts, which
    // derives the same domain for the Cognito callback allowlist (this file can't read CDK
    // context, so the string is duplicated deliberately).
    const hostedZoneDomainName = "dev.keveenmenezes.com";
    const domainName = `duckstore.${hostedZoneDomainName}`;
    // Image CDN (ADR-0034) — same deterministic-domain trick as the SPA/admin, so the
    // URL is known without reading the DuckStoreProductImagesStack outputs.
    const imageCdnUrl = `https://img-duckstore.${hostedZoneDomainName}`;

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
    // Needed to validate the `iss` claim of ID tokens before a session is opened
    // (lib/auth/session.ts, ADR-0041 §6).
    const cognitoUserPoolId = aws.cloudformation.getExportOutput({
      name: "DuckStoreAppSyncStack-UserPoolId",
    }).value;

    // BFF-owned session store (ADR-0041). Cognito's access/id tokens are 1h-lived while a session
    // is 30 days; keeping the tokens here — instead of in browser cookies — is what lets them be
    // refreshed transparently, keeps the 30-day refresh token off the client entirely, and makes
    // logout a real revocation rather than a best-effort cookie delete.
    //
    // It lives here rather than under infra/ because it is private to this app: no service reads
    // it, so it has no bounded-context owner, and ADR-0020 put SPA-owned infrastructure in SST.
    // TTL reaps expired sessions at no compute cost, mirroring the guest-cart TTL of ADR-0016 §4.
    const sessions = new sst.aws.Dynamo("Sessions", {
      fields: { SessionId: "string" },
      primaryIndex: { hashKey: "SessionId" },
      ttl: "ExpiresAt",
    });

    // Secret for the generic revalidation webhook
    // (app/api/webhooks/revalidate/route.ts) — verifies the HMAC signature
    // on the `revalidator` Lambda's calls below. That route is
    // server-to-server only (no browser ever calls it — see ADR-0020), so
    // this is the only auth path it has.
    const webhookSecret = new sst.Secret("WebhookSecret");

    // Security headers at the distribution, so they also cover what never reaches the server
    // function: /_next/static/* is served straight from S3, so next.config.mjs's headers() can't
    // reach it. Every header below is `override: false`, which makes CloudFront add it only when
    // the origin didn't — the server function's own (identical) values keep winning for documents
    // and route handlers, and this fills in for the static objects.
    //
    // CSP is deliberately not here: it is the one header whose value depends on the app (the image
    // CDN origin), so next.config.mjs stays its single source of truth rather than having two
    // definitions drift apart.
    const securityHeadersPolicy = new aws.cloudfront.ResponseHeadersPolicy("SpaSecurityHeaders", {
      name: `${environmentName}-duckstore-spa-security-headers`,
      securityHeadersConfig: {
        strictTransportSecurity: {
          accessControlMaxAgeSec: 63072000,
          includeSubdomains: true,
          preload: true,
          override: false,
        },
        contentTypeOptions: { override: false },
        frameOptions: { frameOption: "DENY", override: false },
        referrerPolicy: {
          referrerPolicy: "strict-origin-when-cross-origin",
          override: false,
        },
      },
      // Non-production stages sit on public domains off the same hosted zone, so they would
      // otherwise be crawlable and compete with production for the same content. The header covers
      // what a <meta name="robots"> cannot: RSC payloads, JSON from route handlers, images.
      ...(environmentName === "production"
        ? {}
        : {
            customHeadersConfig: {
              items: [
                { header: "X-Robots-Tag", value: "noindex, nofollow", override: true },
              ],
            },
          }),
    });

    // Extracted so the transform below stays a single reference — the Nextjs component nests
    // transforms one level deeper than an inline closure can express readably.
    // SST's Transform<T> is `(args, opts, name) => undefined` — returning `undefined` explicitly
    // is what makes the signature assignable, since `void` is not.
    const applySecurityHeaders = (
      distributionArgs: aws.cloudfront.DistributionArgs,
    ): undefined => {
      distributionArgs.defaultCacheBehavior = $output(
        distributionArgs.defaultCacheBehavior,
      ).apply((behavior) => ({
        ...behavior,
        responseHeadersPolicyId: securityHeadersPolicy.id,
      }));
      distributionArgs.orderedCacheBehaviors = $output(
        distributionArgs.orderedCacheBehaviors,
      ).apply((behaviors) =>
        (behaviors ?? []).map((behavior) => ({
          ...behavior,
          responseHeadersPolicyId: securityHeadersPolicy.id,
        })),
      );
      return undefined;
    };

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
      // Grants the server function IAM access to the session table; the table name is passed
      // explicitly below since lib/auth/session-store.ts reads it from the environment.
      link: [sessions],
      environment: {
        GRAPHQL_BACKEND: "appsync",
        APPSYNC_URL: appsyncUrl,
        APPSYNC_API_KEY: appsyncApiKey,
        COGNITO_CLIENT_ID: cognitoClientId,
        COGNITO_HOSTED_UI_URL: cognitoHostedUiUrl,
        COGNITO_USER_POOL_ID: cognitoUserPoolId,
        SESSIONS_TABLE_NAME: sessions.name,
        WEBHOOK_SECRET: webhookSecret.value,
        NEXT_PUBLIC_SITE_URL: `https://${domainName}`,
        NEXT_PUBLIC_IMAGE_CDN_URL: imageCdnUrl,
        // Read at build time by next.config.mjs (CSP img-src) and shared/lib/site.ts, which gates
        // robots/sitemap on it — only `production` is allowed to be indexed.
        NEXT_PUBLIC_ENVIRONMENT: environmentName,
      },
      // Attaches the policy above to every cache behavior the Nextjs component creates (server,
      // image optimizer, and the static-asset behaviors).
      transform: {
        cdn: (cdnArgs) => {
          cdnArgs.transform = { ...cdnArgs.transform, distribution: applySecurityHeaders };
          return undefined;
        },
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
        // CatalogViewProduct{Synced,Deleted}Event (ADR-0035) — CatalogView's own CDC events,
        // used for the products/products:{id} tags — replace the previous direct subscription to
        // Catalog/Pricing's upstream events. See the file header comment for why.
        //
        // ReviewCreatedEvent/ReviewUpdatedEvent stay subscribed directly: the reviews:{id} tag
        // covers the raw review list, which lives in Review's own store, not catalogview-products
        // (CatalogView only folds in the aggregate rating) — so there is no CatalogView event to
        // subscribe to instead, and no race to fix on this path (Review's own CDC event already
        // fires only after Review's write commits).
        detailType: [
          "CatalogViewProductSyncedEvent",
          "CatalogViewProductDeletedEvent",
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
