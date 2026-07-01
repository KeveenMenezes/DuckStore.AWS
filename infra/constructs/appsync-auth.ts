import * as cdk from 'aws-cdk-lib';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';

export interface AppSyncAuthProps {
  readonly spaBaseUrl: string;
}

export class AppSyncAuth extends Construct {
  public readonly userPool: cognito.UserPool;
  public readonly userPoolClient: cognito.UserPoolClient;
  public readonly hostedUiUrl: string;

  constructor(scope: Construct, id: string, props: AppSyncAuthProps) {
    super(scope, id);

    this.userPool = new cognito.UserPool(this, 'UserPool', {
      userPoolName: 'duckstore-users',
      selfSignUpEnabled: true,
      signInAliases: { email: true },
      autoVerify: { email: true },
      standardAttributes: {
        email: { required: true, mutable: true },
      },
      passwordPolicy: {
        minLength: 8,
        requireUppercase: true,
        requireLowercase: true,
        requireDigits: true,
        requireSymbols: false,
      },
      accountRecovery: cognito.AccountRecovery.EMAIL_ONLY,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // RBAC groups — checked via ctx.identity.groups in AppSync JS resolvers
    new cognito.CfnUserPoolGroup(this, 'CustomerGroup', {
      userPoolId: this.userPool.userPoolId,
      groupName: 'Customer',
      description: 'Regular store customers',
    });
    new cognito.CfnUserPoolGroup(this, 'SellerGroup', {
      userPoolId: this.userPool.userPoolId,
      groupName: 'Seller',
      description: 'Sellers who manage the catalog',
    });
    new cognito.CfnUserPoolGroup(this, 'AdminGroup', {
      userPoolId: this.userPool.userPoolId,
      groupName: 'Admin',
      description: 'Admins with full access to orders and management',
    });

    // Post-Confirmation trigger: auto-assigns every new verified user to the Customer group.
    const assignCustomerGroupFn = new lambda.Function(this, 'AssignCustomerGroupFn', {
      functionName: 'cognito-assign-customer-group',
      runtime: lambda.Runtime.NODEJS_18_X,
      handler: 'index.handler',
      code: lambda.Code.fromInline(`
const { CognitoIdentityProviderClient, AdminAddUserToGroupCommand } = require('@aws-sdk/client-cognito-identity-provider');
const client = new CognitoIdentityProviderClient({});
exports.handler = async (event) => {
  await client.send(new AdminAddUserToGroupCommand({
    UserPoolId: event.userPoolId,
    Username: event.userName,
    GroupName: 'Customer',
  }));
  return event;
};
      `),
      timeout: cdk.Duration.seconds(10),
    });

    assignCustomerGroupFn.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ['cognito-idp:AdminAddUserToGroup'],
        resources: [this.userPool.userPoolArn],
      }),
    );

    this.userPool.addTrigger(
      cognito.UserPoolOperation.POST_CONFIRMATION,
      assignCustomerGroupFn,
    );

    const domain = this.userPool.addDomain('Domain', {
      cognitoDomain: { domainPrefix: 'duckstore' },
    });

    this.userPoolClient = this.userPool.addClient('SpaClient', {
      userPoolClientName: 'duckstore-spa',
      generateSecret: false,
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [
          cognito.OAuthScope.OPENID,
          cognito.OAuthScope.EMAIL,
          cognito.OAuthScope.PROFILE,
        ],
        callbackUrls: [`${props.spaBaseUrl}/api/auth/callback`],
        logoutUrls: [props.spaBaseUrl],
      },
      idTokenValidity: cdk.Duration.hours(1),
      accessTokenValidity: cdk.Duration.hours(1),
      refreshTokenValidity: cdk.Duration.days(30),
      preventUserExistenceErrors: true,
    });

    this.hostedUiUrl = `https://${domain.domainName}.auth.${cdk.Stack.of(this).region}.amazoncognito.com`;
  }
}
