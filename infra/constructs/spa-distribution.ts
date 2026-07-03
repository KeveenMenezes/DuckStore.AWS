import * as cdk from 'aws-cdk-lib';
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
  readonly productImagesBucket: s3.Bucket;
  readonly defaultServerFunctionUrl: lambda.FunctionUrl;
  readonly imageOptimizationFunctionUrl: lambda.FunctionUrl;
  /** Full custom domain, e.g. "dev-duckstore.keveenmenezes.com". */
  readonly domainName: string;
  /** Apex domain of the Route53 hosted zone, e.g. "keveenmenezes.com". */
  readonly hostedZoneDomainName: string;
  /** Sent as the x-origin-verify custom origin header to the server function; see spa-stack.ts. */
  readonly originVerifySecret: string;
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
      productImagesBucket,
      defaultServerFunctionUrl,
      imageOptimizationFunctionUrl,
      domainName,
      hostedZoneDomainName,
      originVerifySecret,
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
    // Dedicated product-images bucket — served as pure CDN, outside the Next
    // image optimizer (ADR-0018).
    const productImagesOrigin = origins.S3BucketOrigin.withOriginAccessControl(productImagesBucket);
    // Plain origins, not withOriginAccessControl — the Function URLs are
    // NONE auth (see spa-lambdas.ts), so there's no SigV4 identity for OAC
    // to sign with. The server origin instead gets a secret custom header
    // that middleware.ts checks.
    const serverOrigin = new origins.FunctionUrlOrigin(defaultServerFunctionUrl, {
      customHeaders: { 'x-origin-verify': originVerifySecret },
    });
    const imageOrigin = new origins.FunctionUrlOrigin(imageOptimizationFunctionUrl);

    // The image optimizer varies its response by the Accept header (AVIF vs
    // WebP vs JPEG) and the url/w/q query string. CloudFront must key the cache
    // on both, or it would serve the wrong format to a client — hence a
    // dedicated policy instead of the CACHING_DISABLED the behavior used while
    // optimization was off.
    const imageCachePolicy = new cloudfront.CachePolicy(this, 'ImageOptimizationCachePolicy', {
      minTtl: cdk.Duration.seconds(0),
      defaultTtl: cdk.Duration.days(365),
      maxTtl: cdk.Duration.days(365),
      queryStringBehavior: cloudfront.CacheQueryStringBehavior.allowList('url', 'w', 'q'),
      headerBehavior: cloudfront.CacheHeaderBehavior.allowList('Accept'),
      cookieBehavior: cloudfront.CacheCookieBehavior.none(),
      enableAcceptEncodingGzip: true,
      enableAcceptEncodingBrotli: true,
    });

    const staticBehavior: cloudfront.BehaviorOptions = {
      origin: s3Origin,
      cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
      viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
    };

    // The server function returns per-response Cache-Control: SSG/ISR shells
    // (/cart, /challenges, /checkout, /my-orders, /my-profile) say
    // `s-maxage=31536000`, while dynamic pages (/, /products/[id]) and the API
    // routes say `no-store` (or send no header). CACHING_DISABLED ignored all of
    // that and forced every request onto the Lambda — so cacheable shells never
    // reached the CloudFront edge (all MISS → high TTFB). This policy instead
    // *respects* the origin's Cache-Control (minTtl/defaultTtl = 0, so a missing
    // or no-store header is never cached; only an explicit s-maxage caches, up to
    // maxTtl). The key is kept minimal but includes the headers/query that vary a
    // Next response so full-page HTML and React Server Component (RSC) payloads
    // for the same route cache separately and don't collide. Cookies are excluded
    // from the key — the cached shells are user-agnostic (auth/user data hydrates
    // client-side); cookies are still forwarded to the origin for dynamic pages
    // via the ALL_VIEWER_EXCEPT_HOST_HEADER origin-request policy below.
    const serverCachePolicy = new cloudfront.CachePolicy(this, 'ServerCachePolicy', {
      minTtl: cdk.Duration.seconds(0),
      defaultTtl: cdk.Duration.seconds(0),
      maxTtl: cdk.Duration.days(365),
      queryStringBehavior: cloudfront.CacheQueryStringBehavior.all(),
      headerBehavior: cloudfront.CacheHeaderBehavior.allowList(
        'accept',
        'rsc',
        'next-router-prefetch',
        'next-router-state-tree',
        'next-url',
        'x-prerender-revalidate',
      ),
      cookieBehavior: cloudfront.CacheCookieBehavior.none(),
      enableAcceptEncodingGzip: true,
      enableAcceptEncodingBrotli: true,
    });

    // Lambda Function URL origins must not receive the CloudFront-facing Host
    // header verbatim — this AWS-managed policy exists specifically for that.
    const serverBehavior: cloudfront.BehaviorOptions = {
      origin: serverOrigin,
      cachePolicy: serverCachePolicy,
      originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
      allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL,
      viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
    };

    this.distribution = new cloudfront.Distribution(this, 'Distribution', {
      defaultBehavior: serverBehavior,
      additionalBehaviors: {
        '_next/image*': {
          origin: imageOrigin,
          cachePolicy: imageCachePolicy,
          originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
          allowedMethods: cloudfront.AllowedMethods.ALLOW_GET_HEAD,
          viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        },
        // Product catalog images — straight from S3, no optimizer Lambda (ADR-0018).
        // More specific than the default behavior, distinct from `images/*` (public assets).
        'product-images/*': {
          origin: productImagesOrigin,
          cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
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
