import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { ChallengesDynamoDB } from '../constructs/challenges-dynamodb';
import { ChallengesLambdas } from '../constructs/challenges-lambdas';

export class ChallengesStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const dynamoDB = new ChallengesDynamoDB(this, 'ChallengesDynamoDB');

    const lambdas = new ChallengesLambdas(this, 'ChallengesLambdas', {
      challengesTable: dynamoDB.challengesTable,
      challengeProgressTable: dynamoDB.challengeProgressTable,
    });

    new cdk.CfnOutput(this, 'ChallengesTableName', {
      value: dynamoDB.challengesTable.tableName,
      exportName: `${this.stackName}-ChallengesTable`,
    });
    new cdk.CfnOutput(this, 'ChallengeProgressTableName', {
      value: dynamoDB.challengeProgressTable.tableName,
      exportName: `${this.stackName}-ChallengeProgressTable`,
    });
    new cdk.CfnOutput(this, 'ChallengesProgressStreamPublisherArn', {
      value: lambdas.challengesProgressStreamPublisher.functionArn,
      exportName: `${this.stackName}-ChallengesProgressStreamPublisherArn`,
    });
  }
}
