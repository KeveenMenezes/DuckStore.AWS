import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { UserDynamoDB } from '../constructs/user-dynamodb';
import { UserLambdas } from '../constructs/user-lambdas';

export class UserStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new UserDynamoDB(this, 'UserDynamoDB');

    const lambdas = new UserLambdas(this, 'UserLambdas', {
      userProfilesTable: dynamoDB.userProfilesTable,
    });

    new cdk.CfnOutput(this, 'UserProfilesTableName', {
      value: dynamoDB.userProfilesTable.tableName,
      exportName: `${this.stackName}-UserProfilesTable`,
    });
    new cdk.CfnOutput(this, 'GetProfileArn', {
      value: lambdas.getProfile.functionArn,
      exportName: `${this.stackName}-GetProfileArn`,
    });
  }
}
