import * as fs from 'fs';
import * as path from 'path';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as cr from 'aws-cdk-lib/custom-resources';
import { Construct } from 'constructs';

export interface SpaInvalidationProps {
  readonly distribution: cloudfront.IDistribution;
  /**
   * Path to the SPA's `.open-next` build output. Used to read the Next.js
   * build ID (assets/BUILD_ID), which changes on every rebuild and drives
   * whether an invalidation actually fires (see below).
   */
  readonly openNextDir: string;
}

/**
 * Invalidates the CloudFront edge cache after a deploy.
 *
 * The static JS/CSS chunks under `_next/*` are content-hashed and immutable, so
 * they never go stale — but the HTML/RSC page shells served by the server
 * Lambda are cached at the edge with `s-maxage=31536000` (see the ServerCachePolicy
 * in spa-distribution.ts, maxTtl = 365 days). Those cached shells hard-reference
 * the *previous* build's chunk filenames, so without an invalidation CloudFront
 * keeps serving old HTML (which loads old, possibly-broken JS) for up to a year
 * after new code is deployed. A BucketDeployment can't own this because the
 * distribution is created after the bucket, hence a dedicated custom resource.
 *
 * The invalidation is keyed on the OpenNext BUILD_ID: the physical resource id
 * only changes when a new build is deployed, so `cdk deploy` runs that don't
 * change the frontend don't fire (and pay for) a redundant invalidation.
 * `/*` is deliberately broad — it also refreshes the immutable chunks, which is
 * harmless (they revalidate straight back from S3) and keeps the rule simple.
 */
export class SpaInvalidation extends Construct {
  constructor(scope: Construct, id: string, props: SpaInvalidationProps) {
    super(scope, id);

    const { distribution, openNextDir } = props;

    const buildId = fs.readFileSync(path.join(openNextDir, 'assets', 'BUILD_ID'), 'utf8').trim();

    new cr.AwsCustomResource(this, 'Resource', {
      onUpdate: {
        service: 'CloudFront',
        action: 'createInvalidation',
        parameters: {
          DistributionId: distribution.distributionId,
          InvalidationBatch: {
            // Must be unique per request; the build ID guarantees a fresh value
            // exactly when there's new content to invalidate.
            CallerReference: buildId,
            Paths: { Quantity: 1, Items: ['/*'] },
          },
        },
        // Tying the physical id to the build ID means CloudFormation only treats
        // this as changed (and re-runs the invalidation) on a new build.
        physicalResourceId: cr.PhysicalResourceId.of(`invalidation-${buildId}`),
      },
      policy: cr.AwsCustomResourcePolicy.fromStatements([
        new iam.PolicyStatement({
          actions: ['cloudfront:CreateInvalidation'],
          resources: [distribution.distributionArn],
        }),
      ]),
    });
  }
}
