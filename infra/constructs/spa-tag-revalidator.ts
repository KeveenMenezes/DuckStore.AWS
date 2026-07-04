import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';

export interface SpaTagRevalidatorProps {
  readonly tagCacheTable: dynamodb.Table;
  /** The shared "duckstore-event-bus" Catalog/Review publish integration events to. */
  readonly eventBus: events.IEventBus;
  /**
   * Path to the SPA's `.open-next` build output. Used to read the Next.js
   * build ID (assets/BUILD_ID), which OpenNext prefixes onto every tag-cache
   * partition key ("{buildId}/products") — the revalidator must query with
   * the same prefix or it matches nothing. Changes on every deploy, so it's
   * read fresh at synth time rather than hardcoded.
   */
  readonly openNextDir: string;
  /**
   * The distribution serving the SPA. `/` and `/products/[id]` are prerendered
   * ISR routes (see prerender-manifest.json) that come back from the origin
   * with `Cache-Control: s-maxage=31536000` (spa-distribution.ts's
   * ServerCachePolicy caches that for up to a year at the edge) — marking the
   * DynamoDB tag-cache stale only affects what the origin Lambda serves on its
   * *next* invocation, so an edge PoP that already cached the page keeps
   * serving the old HTML for up to a year unless this Lambda also invalidates
   * the affected CloudFront paths directly.
   */
  readonly distribution: cloudfront.IDistribution;
}

/**
 * Replaces the old HTTP webhook (public POST to /api/webhooks/catalog-updated
 * + a shared secret header) for backend-triggered ISR revalidation.
 * Subscribes directly to CatalogUpdatedEvent/ReviewCreatedEvent on the same
 * EventBridge bus Catalog already publishes to, and marks every tag-cache
 * entry for the affected tag as stale directly in DynamoDB — the same
 * mechanism Next.js's own revalidateTag() uses internally (see
 * lambda/spa-tag-revalidator/index.mjs for the full explanation of why this
 * replaces both the original HTTP webhook and an earlier SQS-based design).
 * No public HTTP surface, no secret to manage, no dependency on the SPA even
 * being reachable to receive the trigger — hence no "webhook" in the name.
 *
 * Also invalidates the affected CloudFront paths directly: `/` and
 * `/products/[id]` are ISR routes cached at the edge for up to a year
 * (`s-maxage=31536000`, confirmed in prod via response headers), so marking
 * the DynamoDB tag-cache stale alone never reaches a PoP that already has the
 * page cached — see the `distribution` prop doc above.
 *
 * Relies on the Node.js 22 Lambda runtime's built-in AWS SDK v3 (no
 * node_modules bundled) — acceptable for this small piece of internal glue
 * in a demo project; see AWS's Node.js Lambda docs on runtime-included SDK
 * versions if this ever needs pinning.
 */
export class SpaTagRevalidator extends Construct {
  public readonly function: lambda.Function;

  constructor(scope: Construct, id: string, props: SpaTagRevalidatorProps) {
    super(scope, id);

    const { tagCacheTable, eventBus, openNextDir, distribution } = props;

    const buildId = fs.readFileSync(path.join(openNextDir, 'assets', 'BUILD_ID'), 'utf8').trim();

    this.function = new lambda.Function(this, 'Function', {
      runtime: lambda.Runtime.NODEJS_22_X,
      architecture: lambda.Architecture.ARM_64,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(path.join(__dirname, '..', 'lambda', 'spa-tag-revalidator')),
      timeout: cdk.Duration.seconds(30),
      memorySize: 256,
      description:
        'Marks OpenNext tag-cache entries stale and invalidates the corresponding CloudFront paths, in response to CatalogUpdatedEvent/ReviewCreatedEvent',
      environment: {
        TAG_CACHE_TABLE_NAME: tagCacheTable.tableName,
        TAG_CACHE_BUILD_ID: buildId,
        CLOUDFRONT_DISTRIBUTION_ID: distribution.distributionId,
      },
    });

    tagCacheTable.grantReadWriteData(this.function);

    this.function.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ['cloudfront:CreateInvalidation'],
        resources: [distribution.distributionArn],
      }),
    );

    // Deploy-order note: this imports the bus by fixed name (no CFN
    // Fn::ImportValue), so CDK won't sequence stack deploys for us —
    // duckstore-event-bus must already exist (CatalogStack deploys it).
    // True for every environment this has run in so far.
    const rule = new events.Rule(this, 'Rule', {
      eventBus,
      ruleName: 'spa-tag-revalidator-rule',
      description: 'Routes CatalogUpdatedEvent/ReviewCreatedEvent to the SPA ISR tag revalidator Lambda',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['CatalogUpdatedEvent', 'ReviewCreatedEvent'],
      },
    });
    rule.addTarget(new targets.LambdaFunction(this.function));
  }
}
