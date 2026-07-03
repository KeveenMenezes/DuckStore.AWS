import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { AppSyncAuth } from '../constructs/appsync-auth';
import { AppSyncApi } from '../constructs/appsync-api';

export interface AppSyncStackProps extends cdk.StackProps {
  readonly spaBaseUrls: string[];
}

export class AppSyncStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: AppSyncStackProps) {
    super(scope, id, props);

    const auth = new AppSyncAuth(this, 'AppSyncAuth', {
      spaBaseUrls: props.spaBaseUrls,
    });

    const appsync = new AppSyncApi(this, 'AppSyncApi', {
      userPool: auth.userPool,
    });

    new cdk.CfnOutput(this, 'ApiUrl', {
      value: appsync.api.graphqlUrl,
      exportName: `${this.stackName}-ApiUrl`,
      description: 'AppSync GraphQL endpoint — set as APPSYNC_URL env var in the SPA (server-only)',
    });

    new cdk.CfnOutput(this, 'ApiKey', {
      value: appsync.api.apiKey ?? '',
      exportName: `${this.stackName}-ApiKey`,
      description: 'AppSync API key for public catalog reads — set as APPSYNC_API_KEY env var in the SPA',
    });

    new cdk.CfnOutput(this, 'UserPoolId', {
      value: auth.userPool.userPoolId,
      exportName: `${this.stackName}-UserPoolId`,
    });

    new cdk.CfnOutput(this, 'UserPoolClientId', {
      value: auth.userPoolClient.userPoolClientId,
      exportName: `${this.stackName}-UserPoolClientId`,
      description: 'Set as COGNITO_CLIENT_ID env var in the SPA',
    });

    new cdk.CfnOutput(this, 'HostedUiUrl', {
      value: auth.hostedUiUrl,
      exportName: `${this.stackName}-HostedUiUrl`,
      description: 'Set as COGNITO_HOSTED_UI_URL env var in the SPA',
    });
  }
}
