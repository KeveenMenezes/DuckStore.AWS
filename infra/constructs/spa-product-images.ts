import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import { Construct } from 'constructs';

/**
 * Dedicated bucket for product catalog images, served through the SPA's own
 * CloudFront distribution at `/product-images/*` (see spa-distribution.ts) so
 * they live on the same custom domain the SPA does. That domain is whitelisted
 * in next.config.mjs `images.remotePatterns`, letting Next's image optimizer
 * fetch and re-encode them (AVIF/WebP) — the seeder's product ImageUrl points
 * here instead of the old external CDN (see CatalogInitialData.cs).
 *
 * Same private-bucket + OAC pattern as SpaStorage — never public.
 *
 * NOTE: `assets/product-images/hero.png` is currently a placeholder — replace
 * it with the real product image(s); the seeder references `hero.png`.
 */
export class SpaProductImages extends Construct {
  public readonly bucket: s3.Bucket;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    this.bucket = new s3.Bucket(this, 'Bucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    // Objects live under a `product-images/` key prefix so the CloudFront
    // `product-images/*` behavior (S3 origin, no originPath) maps a request for
    // `/product-images/hero.png` straight to the matching S3 key.
    new s3deploy.BucketDeployment(this, 'DeployImages', {
      sources: [s3deploy.Source.asset(path.join(__dirname, '..', 'assets', 'product-images'))],
      destinationBucket: this.bucket,
      destinationKeyPrefix: 'product-images',
      cacheControl: [s3deploy.CacheControl.fromString('public,max-age=31536000,immutable')],
      prune: false,
    });
  }
}
