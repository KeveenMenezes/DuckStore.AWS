import * as crypto from 'crypto';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';
import { SpaStorage } from '../constructs/spa-storage';
import { SpaLambdas } from '../constructs/spa-lambdas';
import { SpaDistribution } from '../constructs/spa-distribution';
import { SpaRevalidationWebhook } from '../constructs/spa-revalidation-webhook';

const REPO_ROOT = path.join(__dirname, '..', '..');
const OPEN_NEXT_DIR = path.join(REPO_ROOT, 'src/WebApps/Shopping.Web.SPA.React/.open-next');

export interface SpaStackProps extends cdk.StackProps {
  /** Prefixes the custom domain, e.g. "dev" -> dev-duckstore.keveenmenezes.com. */
  readonly environmentName: string;
  readonly hostedZoneDomainName: string;
  readonly appsyncUrl: string;
  readonly appsyncApiKey: string;
  readonly cognitoClientId: string;
  readonly cognitoHostedUiUrl: string;
}

export class SpaStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: SpaStackProps) {
    super(scope, id, props);

    // catalog-updated no longer has an HTTP webhook (see SpaRevalidationWebhook
    // below) — this is only the client-triggered review-created path, POSTed
    // by review-form.tsx right after a user submits a review.
    const reviewWebhookSecret = new cdk.CfnParameter(this, 'ReviewWebhookSecret', {
      type: 'String',
      noEcho: true,
      description: 'Secret the review-created webhook validates in x-webhook-secret.',
    });

    const domainName = `${props.environmentName}-duckstore.${props.hostedZoneDomainName}`;

    // Lambda Function URLs with AWS_IAM auth + CloudFront OAC can't carry a
    // POST/PUT body: CloudFront's SigV4 signing to a Function URL origin
    // requires the *client* to precompute an x-amz-content-sha256 payload
    // hash, which neither browsers nor the Catalog/Review webhook-sending
    // Lambdas do (AWS docs: "Lambda doesn't support unsigned payloads" for
    // Function URL OAC). That breaks every POST route (GraphQL BFF,
    // webhooks). Instead the server Function URL is public (NONE auth, like
    // most real-world Next.js-on-Lambda deployments) and CloudFront injects
    // a secret header that middleware.ts on the SPA rejects requests
    // without — regenerated per deploy, only needs to match between here
    // and the distribution's custom origin header.
    const originVerifySecret = crypto.randomBytes(32).toString('hex');

    const storage = new SpaStorage(this, 'SpaStorage', {
      openNextDir: OPEN_NEXT_DIR,
    });

    const functions = new SpaLambdas(this, 'SpaLambdas', {
      openNextDir: OPEN_NEXT_DIR,
      assetsBucket: storage.assetsBucket,
      tagCacheTable: storage.tagCacheTable,
      revalidationQueue: storage.revalidationQueue,
      originVerifySecret,
      appEnvironment: {
        GRAPHQL_BACKEND: 'appsync',
        APPSYNC_URL: props.appsyncUrl,
        APPSYNC_API_KEY: props.appsyncApiKey,
        COGNITO_CLIENT_ID: props.cognitoClientId,
        COGNITO_HOSTED_UI_URL: props.cognitoHostedUiUrl,
        REVIEW_WEBHOOK_SECRET: reviewWebhookSecret.valueAsString,
        NEXT_PUBLIC_SITE_URL: `https://${domainName}`,
      },
    });

    const distribution = new SpaDistribution(this, 'SpaDistribution', {
      assetsBucket: storage.assetsBucket,
      defaultServerFunctionUrl: functions.defaultServerFunctionUrl,
      imageOptimizationFunctionUrl: functions.imageOptimizationFunctionUrl,
      domainName,
      hostedZoneDomainName: props.hostedZoneDomainName,
      originVerifySecret,
    });

    // Backend-triggered ISR revalidation (catalog/review changes from
    // outside the SPA) — no HTTP webhook, straight EventBridge -> DynamoDB
    // tag lookup -> SQS revalidation queue. Replaces the old
    // catalog-catalog-updated-consumer + /api/webhooks/catalog-updated pair.
    new SpaRevalidationWebhook(this, 'RevalidationWebhook', {
      tagCacheTable: storage.tagCacheTable,
      revalidationQueue: storage.revalidationQueue,
      spaHost: domainName,
      eventBus: events.EventBus.fromEventBusName(this, 'DuckstoreEventBus', 'duckstore-event-bus'),
    });

    new cdk.CfnOutput(this, 'SpaUrl', {
      value: distribution.url,
      exportName: `${this.stackName}-SpaUrl`,
      description: 'Public URL of the deployed SPA — use as SPA_BASE_URL in other stacks (e.g. Cognito callback URLs)',
    });

    new cdk.CfnOutput(this, 'CloudFrontDistributionId', {
      value: distribution.distribution.distributionId,
      exportName: `${this.stackName}-CloudFrontDistributionId`,
    });

    new cdk.CfnOutput(this, 'CloudFrontDomainName', {
      value: distribution.distribution.distributionDomainName,
      exportName: `${this.stackName}-CloudFrontDomainName`,
    });
  }
}
