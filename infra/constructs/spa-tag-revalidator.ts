import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';

export interface SpaTagRevalidatorProps {
  readonly tagCacheTable: dynamodb.Table;
  /** The shared "duckstore-event-bus" Catalog/Review publish integration events to. */
  readonly eventBus: events.IEventBus;
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
 * Relies on the Node.js 22 Lambda runtime's built-in AWS SDK v3 (no
 * node_modules bundled) — acceptable for this small piece of internal glue
 * in a demo project; see AWS's Node.js Lambda docs on runtime-included SDK
 * versions if this ever needs pinning.
 */
export class SpaTagRevalidator extends Construct {
  public readonly function: lambda.Function;

  constructor(scope: Construct, id: string, props: SpaTagRevalidatorProps) {
    super(scope, id);

    const { tagCacheTable, eventBus } = props;

    this.function = new lambda.Function(this, 'Function', {
      runtime: lambda.Runtime.NODEJS_22_X,
      architecture: lambda.Architecture.ARM_64,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(path.join(__dirname, '..', 'lambda', 'spa-tag-revalidator')),
      timeout: cdk.Duration.seconds(30),
      memorySize: 256,
      description:
        'Marks OpenNext tag-cache entries stale in response to CatalogUpdatedEvent/ReviewCreatedEvent',
      environment: {
        TAG_CACHE_TABLE_NAME: tagCacheTable.tableName,
      },
    });

    tagCacheTable.grantReadWriteData(this.function);

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
