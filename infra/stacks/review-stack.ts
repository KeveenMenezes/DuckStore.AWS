import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { ReviewDynamoDB } from '../constructs/review-dynamodb';
import { ReviewLambdas } from '../constructs/review-lambdas';

export class ReviewStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new ReviewDynamoDB(this, 'ReviewDynamoDB');

    const lambdas = new ReviewLambdas(this, 'ReviewLambdas', {
      reviewsTable: dynamoDB.reviewsTable,
    });

    new cdk.CfnOutput(this, 'ReviewsTableName', {
      value: dynamoDB.reviewsTable.tableName,
      exportName: `${this.stackName}-ReviewsTable`,
    });
    new cdk.CfnOutput(this, 'ReviewCreatedPublisherArn', {
      value: lambdas.reviewCreatedPublisher.functionArn,
      exportName: `${this.stackName}-ReviewCreatedPublisherArn`,
    });
  }
}
