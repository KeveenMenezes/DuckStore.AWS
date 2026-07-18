import * as cdk from 'aws-cdk-lib';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as targets from 'aws-cdk-lib/aws-route53-targets';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import { Construct } from 'constructs';

export interface ManagementStackProps extends cdk.StackProps {
  /** e.g. dev-management-duckstore.keveenmenezes.com — must match the URL registered as
   *  the management client's Cognito callback in the AppSync stack. */
  readonly managementDomainName: string;
  /** Route53 public hosted zone the management record is created in (keveenmenezes.com). */
  readonly hostedZoneDomainName: string;
}

/**
 * Static-site hosting for the Blazor WASM management app (Managment.Web.Blazor):
 * private S3 bucket behind CloudFront (Origin Access Control). The BucketDeployment
 * reads the app's `dotnet publish` output, so the workflow must publish before
 * `cdk deploy` — and bin/app.ts only instantiates this stack behind the
 * `-c deployManagement=true` context flag so other stacks synth without that folder.
 */
export class ManagementStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: ManagementStackProps) {
    super(scope, id, props);

    const siteBucket = new s3.Bucket(this, 'ManagementSiteBucket', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      autoDeleteObjects: true,
    });

    const zone = route53.HostedZone.fromLookup(this, 'Zone', {
      domainName: props.hostedZoneDomainName,
    });

    // CloudFront requires its certificate in us-east-1 — the whole app already
    // deploys there, so a plain in-stack certificate works.
    const certificate = new acm.Certificate(this, 'ManagementCertificate', {
      domainName: props.managementDomainName,
      validation: acm.CertificateValidation.fromDns(zone),
    });

    const distribution = new cloudfront.Distribution(this, 'ManagementDistribution', {
      comment: 'DuckStore management app (Blazor WASM static site)',
      defaultRootObject: 'index.html',
      domainNames: [props.managementDomainName],
      certificate,
      defaultBehavior: {
        origin: origins.S3BucketOrigin.withOriginAccessControl(siteBucket),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
      },
      // Blazor routes client-side: deep links (e.g. /products/123/edit) miss S3 and
      // come back 403 from the private bucket — serve index.html and let the WASM
      // router resolve the path.
      errorResponses: [
        {
          httpStatus: 403,
          responseHttpStatus: 200,
          responsePagePath: '/index.html',
          ttl: cdk.Duration.seconds(0),
        },
        {
          httpStatus: 404,
          responseHttpStatus: 200,
          responsePagePath: '/index.html',
          ttl: cdk.Duration.seconds(0),
        },
      ],
    });

    new route53.ARecord(this, 'ManagementAliasRecord', {
      zone,
      recordName: props.managementDomainName.replace(`.${props.hostedZoneDomainName}`, ''),
      target: route53.RecordTarget.fromAlias(new targets.CloudFrontTarget(distribution)),
    });

    new s3deploy.BucketDeployment(this, 'DeployManagementSite', {
      sources: [
        s3deploy.Source.asset(
          '../src/WebApps/Managment.Web.Blazor/bin/Release/net10.0/publish/wwwroot',
        ),
      ],
      destinationBucket: siteBucket,
      distribution,
      distributionPaths: ['/*'],
      // Blazor WASM publish output is tens of MB — the default 128 MB deployment
      // Lambda runs out of memory unzipping it.
      memoryLimit: 1024,
    });

    new cdk.CfnOutput(this, 'ManagementUrl', {
      value: `https://${props.managementDomainName}`,
      description: 'Management app URL',
    });

    new cdk.CfnOutput(this, 'DistributionId', {
      value: distribution.distributionId,
    });
  }
}
