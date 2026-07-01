import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as iam from 'aws-cdk-lib/aws-iam';
import { Construct } from 'constructs';
import { SpaStorage } from '../constructs/spa-storage';
import { SpaLambdas } from '../constructs/spa-lambdas';
import { SpaDistribution } from '../constructs/spa-distribution';

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

    // Same shared secrets CatalogStack's CDC consumer sends in x-webhook-secret —
    // the SPA's webhook route handlers validate against these.
    const catalogWebhookSecret = new cdk.CfnParameter(this, 'CatalogWebhookSecret', {
      type: 'String',
      noEcho: true,
      description: 'Secret the catalog-updated webhook validates in x-webhook-secret.',
    });
    const reviewWebhookSecret = new cdk.CfnParameter(this, 'ReviewWebhookSecret', {
      type: 'String',
      noEcho: true,
      description: 'Secret the review-created webhook validates in x-webhook-secret.',
    });

    const domainName = `${props.environmentName}-duckstore.${props.hostedZoneDomainName}`;

    const storage = new SpaStorage(this, 'SpaStorage', {
      openNextDir: OPEN_NEXT_DIR,
    });

    const functions = new SpaLambdas(this, 'SpaLambdas', {
      openNextDir: OPEN_NEXT_DIR,
      assetsBucket: storage.assetsBucket,
      tagCacheTable: storage.tagCacheTable,
      revalidationQueue: storage.revalidationQueue,
      appEnvironment: {
        GRAPHQL_BACKEND: 'appsync',
        APPSYNC_URL: props.appsyncUrl,
        APPSYNC_API_KEY: props.appsyncApiKey,
        COGNITO_CLIENT_ID: props.cognitoClientId,
        COGNITO_HOSTED_UI_URL: props.cognitoHostedUiUrl,
        CATALOG_WEBHOOK_SECRET: catalogWebhookSecret.valueAsString,
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
    });

    // As of Oct 2025, Function URL invocation requires BOTH
    // lambda:InvokeFunctionUrl (granted automatically by
    // FunctionUrlOrigin.withOriginAccessControl) AND lambda:InvokeFunction
    // scoped to InvokedViaFunctionUrl — the CDK OAC helper doesn't add this
    // second grant yet, so without it CloudFront gets a 403 AccessDeniedException.
    const cloudfrontPrincipal = new iam.ServicePrincipal('cloudfront.amazonaws.com');
    for (const fn of [functions.defaultServerFunction, functions.imageOptimizationFunction]) {
      fn.addPermission('AllowCloudFrontInvokeFunction', {
        principal: cloudfrontPrincipal,
        action: 'lambda:InvokeFunction',
        sourceArn: distribution.distribution.distributionArn,
        invokedViaFunctionUrl: true,
      });
    }

    new cdk.CfnOutput(this, 'SpaUrl', {
      value: distribution.url,
      exportName: `${this.stackName}-SpaUrl`,
      description: 'Public URL of the deployed SPA — use as SPA_BASE_URL/SPA_WEBHOOK_URL in other stacks',
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
