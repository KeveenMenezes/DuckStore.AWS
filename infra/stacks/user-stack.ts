import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { UserDynamoDB } from '../constructs/user-dynamodb';

// No Lambda functions — myProfile/updateProfile are AppSync direct DynamoDB resolvers (ADR-0009).
// This stack only provisions the user-profiles table.
export class UserStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new UserDynamoDB(this, 'UserDynamoDB');

    new cdk.CfnOutput(this, 'UserProfilesTableName', {
      value: dynamoDB.userProfilesTable.tableName,
      exportName: `${this.stackName}-UserProfilesTable`,
    });
  }
}
