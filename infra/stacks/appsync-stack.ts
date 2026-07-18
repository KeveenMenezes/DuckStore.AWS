import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import { AppSyncAuth } from '../constructs/appsync-auth';
import { AppSyncApi } from '../constructs/appsync-api';
import { ProductCreateSaga } from '../constructs/product-create-saga';

export interface AppSyncStackProps extends cdk.StackProps {
  readonly spaBaseUrls: string[];
  readonly managementBaseUrls: string[];
  readonly googleClientId?: string;
  readonly amazonClientId?: string;
}

export class AppSyncStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: AppSyncStackProps) {
    super(scope, id, props);

    // Federation client secrets arrive as NoEcho CloudFormation parameters, supplied
    // by the deploy workflow from GitHub Environment secrets via `cdk deploy
    // --parameters` — CloudFormation rejects {{resolve:ssm-secure:...}} on Cognito's
    // ProviderDetails/client_secret, and Secrets Manager costs $0.40/secret/month.
    // NoEcho keeps the value out of the template, console, and logs. The empty
    // default keeps synth/deploy working when federation isn't configured, but a
    // deploy that has the clientId context WITHOUT the matching parameter silently
    // produces an IdP with an empty secret (sign-in with that provider breaks).
    const googleClientSecret = new cdk.CfnParameter(this, 'GoogleClientSecret', {
      type: 'String',
      noEcho: true,
      default: '',
      description: 'Google OAuth client secret for Cognito federation',
    });
    const amazonClientSecret = new cdk.CfnParameter(this, 'AmazonClientSecret', {
      type: 'String',
      noEcho: true,
      default: '',
      description: 'Login with Amazon client secret for Cognito federation',
    });

    const auth = new AppSyncAuth(this, 'AppSyncAuth', {
      spaBaseUrls: props.spaBaseUrls,
      managementBaseUrls: props.managementBaseUrls,
      googleClientId: props.googleClientId,
      googleClientSecret: cdk.SecretValue.cfnParameter(googleClientSecret),
      amazonClientId: props.amazonClientId,
      amazonClientSecret: cdk.SecretValue.cfnParameter(amazonClientSecret),
    });

    const productCreateSaga = new ProductCreateSaga(this, 'ProductCreateSaga');

    const appsync = new AppSyncApi(this, 'AppSyncApi', {
      shoppingUserPool: auth.shoppingUserPool,
      managementUserPool: auth.managementUserPool,
      productCreateSaga: productCreateSaga.stateMachine,
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
      value: auth.shoppingUserPool.userPoolId,
      exportName: `${this.stackName}-UserPoolId`,
    });

    new cdk.CfnOutput(this, 'UserPoolClientId', {
      value: auth.shoppingUserPoolClient.userPoolClientId,
      exportName: `${this.stackName}-UserPoolClientId`,
      description: 'Set as COGNITO_CLIENT_ID env var in the SPA',
    });

    new cdk.CfnOutput(this, 'HostedUiUrl', {
      value: auth.shoppingHostedUiUrl,
      exportName: `${this.stackName}-HostedUiUrl`,
      description: 'Set as COGNITO_HOSTED_UI_URL env var in the SPA',
    });

    new cdk.CfnOutput(this, 'ManagementUserPoolId', {
      value: auth.managementUserPool.userPoolId,
      exportName: `${this.stackName}-ManagementUserPoolId`,
      description:
        'Cognito user pool for staff (Admin/Seller) — login-only, users created via AWS Console. Written into the Blazor appsettings.json (Auth:Authority) at deploy',
    });

    new cdk.CfnOutput(this, 'ManagementUserPoolClientId', {
      value: auth.managementUserPoolClient.userPoolClientId,
      exportName: `${this.stackName}-ManagementUserPoolClientId`,
      description:
        'Cognito app client for the Blazor management app — written into its appsettings.json (Auth:ClientId) at deploy',
    });

    new cdk.CfnOutput(this, 'ManagementHostedUiUrl', {
      value: auth.managementHostedUiUrl,
      exportName: `${this.stackName}-ManagementHostedUiUrl`,
    });
  }
}
