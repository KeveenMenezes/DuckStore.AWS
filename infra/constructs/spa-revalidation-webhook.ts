import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';

export interface SpaRevalidationWebhookProps {
  readonly tagCacheTable: dynamodb.Table;
  readonly revalidationQueue: sqs.Queue;
  /** SPA's public hostname, e.g. "dev-duckstore.keveenmenezes.com" (no scheme). */
  readonly spaHost: string;
  /** The shared "duckstore-event-bus" Catalog/Review publish integration events to. */
  readonly eventBus: events.IEventBus;
}

/**
 * Replaces the old HTTP webhook (public POST to /api/webhooks/catalog-updated
 * + a shared secret header) for backend-triggered ISR revalidation.
 * Subscribes directly to CatalogUpdatedEvent/ReviewCreatedEvent on the same
 * EventBridge bus Catalog already publishes to, resolves affected paths via
 * the OpenNext tag cache table, and enqueues them straight onto the
 * revalidation SQS queue OpenNext's own revalidation Lambda already
 * consumes — no public HTTP surface, no secret to manage, no dependency on
 * the SPA even being reachable to receive the trigger.
 *
 * Relies on the Node.js 22 Lambda runtime's built-in AWS SDK v3 (no
 * node_modules bundled) — acceptable for this small piece of internal glue
 * in a demo project; see AWS's Node.js Lambda docs on runtime-included SDK
 * versions if this ever needs pinning.
 */
export class SpaRevalidationWebhook extends Construct {
  public readonly function: lambda.Function;

  constructor(scope: Construct, id: string, props: SpaRevalidationWebhookProps) {
    super(scope, id);

    const { tagCacheTable, revalidationQueue, spaHost, eventBus } = props;

    this.function = new lambda.Function(this, 'Function', {
      runtime: lambda.Runtime.NODEJS_22_X,
      architecture: lambda.Architecture.ARM_64,
      handler: 'index.handler',
      code: lambda.Code.fromAsset(path.join(__dirname, '..', 'lambda', 'spa-revalidation-webhook')),
      timeout: cdk.Duration.seconds(30),
      memorySize: 256,
      description:
        'Resolves CatalogUpdatedEvent/ReviewCreatedEvent to affected ISR paths and enqueues them for revalidation',
      environment: {
        TAG_CACHE_TABLE_NAME: tagCacheTable.tableName,
        REVALIDATION_QUEUE_URL: revalidationQueue.queueUrl,
        SPA_HOST: spaHost,
      },
    });

    tagCacheTable.grantReadData(this.function);
    revalidationQueue.grantSendMessages(this.function);

    // Deploy-order note: this imports the bus by fixed name (no CFN
    // Fn::ImportValue), so CDK won't sequence stack deploys for us —
    // duckstore-event-bus must already exist (CatalogStack deploys it).
    // True for every environment this has run in so far.
    const rule = new events.Rule(this, 'Rule', {
      eventBus,
      ruleName: 'spa-revalidation-webhook-rule',
      description: 'Routes CatalogUpdatedEvent/ReviewCreatedEvent to the SPA ISR revalidation Lambda',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['CatalogUpdatedEvent', 'ReviewCreatedEvent'],
      },
    });
    rule.addTarget(new targets.LambdaFunction(this.function));
  }
}
