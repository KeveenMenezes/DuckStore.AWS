import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as targets from 'aws-cdk-lib/aws-route53-targets';
import * as s3 from 'aws-cdk-lib/aws-s3';
import { Construct } from 'constructs';

export interface SpaDistributionProps {
  readonly assetsBucket: s3.Bucket;
  readonly defaultServerFunctionUrl: lambda.FunctionUrl;
  readonly imageOptimizationFunctionUrl: lambda.FunctionUrl;
  /** Full custom domain, e.g. "dev-duckstore.keveenmenezes.com". */
  readonly domainName: string;
  /** Apex domain of the Route53 hosted zone, e.g. "keveenmenezes.com". */
  readonly hostedZoneDomainName: string;
}

// Static asset paths served straight from S3 (no Lambda involved) — mirrors
// the `behaviors` array in OpenNext's `open-next.output.json` for this build.
const STATIC_ASSET_PATTERNS = [
  'BUILD_ID',
  '_next/*',
  'icon.svg',
  'images/*',
  'placeholder-logo.png',
  'placeholder-logo.svg',
  'placeholder-user.jpg',
  'placeholder.jpg',
  'placeholder.svg',
];

/**
 * CloudFront distribution wiring the OpenNext origins together, per the
 * `behaviors` routing table in `.open-next/open-next.output.json`:
 * static assets → S3, `_next/image*` → image optimizer, everything else
 * (including `_next/data/*`) → the server function.
 */
export class SpaDistribution extends Construct {
  public readonly distribution: cloudfront.Distribution;
  public readonly url: string;

  constructor(scope: Construct, id: string, props: SpaDistributionProps) {
    super(scope, id);

    const {
      assetsBucket,
      defaultServerFunctionUrl,
      imageOptimizationFunctionUrl,
      domainName,
      hostedZoneDomainName,
    } = props;

    const hostedZone = route53.HostedZone.fromLookup(this, 'Zone', {
      domainName: hostedZoneDomainName,
    });

    // CloudFront requires the certificate in us-east-1; the whole app (and
    // this stack) already deploys there, so no cross-region indirection needed.
    const certificate = new acm.Certificate(this, 'Certificate', {
      domainName,
      validation: acm.CertificateValidation.fromDns(hostedZone),
    });

    const s3Origin = origins.S3BucketOrigin.withOriginAccessControl(assetsBucket, {
      originPath: '/_assets',
    });
    const serverOrigin = origins.FunctionUrlOrigin.withOriginAccessControl(defaultServerFunctionUrl);
    const imageOrigin = origins.FunctionUrlOrigin.withOriginAccessControl(imageOptimizationFunctionUrl);

    const staticBehavior: cloudfront.BehaviorOptions = {
      origin: s3Origin,
      cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
      viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
    };

    // Lambda Function URL origins must not receive the CloudFront-facing Host
    // header verbatim — this AWS-managed policy exists specifically for that.
    const serverBehavior: cloudfront.BehaviorOptions = {
      origin: serverOrigin,
      cachePolicy: cloudfront.CachePolicy.CACHING_DISABLED,
      originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
      allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL,
      viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
    };

    this.distribution = new cloudfront.Distribution(this, 'Distribution', {
      defaultBehavior: serverBehavior,
      additionalBehaviors: {
        '_next/image*': {
          origin: imageOrigin,
          cachePolicy: cloudfront.CachePolicy.CACHING_DISABLED,
          originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
          allowedMethods: cloudfront.AllowedMethods.ALLOW_GET_HEAD,
          viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        },
        '_next/data/*': serverBehavior,
        ...Object.fromEntries(STATIC_ASSET_PATTERNS.map(pattern => [pattern, staticBehavior])),
      },
      domainNames: [domainName],
      certificate,
      defaultRootObject: '',
    });

    new route53.ARecord(this, 'AliasRecordV4', {
      zone: hostedZone,
      recordName: domainName,
      target: route53.RecordTarget.fromAlias(new targets.CloudFrontTarget(this.distribution)),
    });
    new route53.AaaaRecord(this, 'AliasRecordV6', {
      zone: hostedZone,
      recordName: domainName,
      target: route53.RecordTarget.fromAlias(new targets.CloudFrontTarget(this.distribution)),
    });

    this.url = `https://${domainName}`;
  }
}
