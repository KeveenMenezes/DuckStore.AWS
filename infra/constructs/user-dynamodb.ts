import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class UserDynamoDB extends Construct {
  public readonly userProfilesTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Extended customer profile owned by the User bounded context (ADR-0017).
    // PK is the Cognito sub (raw, no prefix) — the profile is always authenticated.
    // No stream/TTL: profiles are lazily provisioned and never expire.
    this.userProfilesTable = new dynamodb.Table(this, 'UserProfilesTable', {
      tableName: 'user-profiles',
      partitionKey: { name: 'UserId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
