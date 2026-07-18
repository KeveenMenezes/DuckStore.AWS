import * as cdk from 'aws-cdk-lib';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import { Construct } from 'constructs';

export interface AppSyncAuthProps {
  /** Base URLs (e.g. http://localhost:3000, https://dev-duckstore.example.com)
   *  the SPA is served from — each gets a Cognito callback + logout URL. */
  readonly spaBaseUrls: string[];
  /** Base URLs the Blazor management app is served from (e.g. https://localhost:7300,
   *  https://dev-management-duckstore.example.com). The OIDC library's callback paths
   *  are fixed at /authentication/{login,logout}-callback. */
  readonly managementBaseUrls: string[];
  /** Google/Amazon federation credentials, passed via CDK context at deploy time
   *  (never committed). When a provider's clientId is set, that "Sign in with X"
   *  button is added to Managed Login. Add the Cognito redirect URI
   *  (`<hostedUi>/oauth2/idpresponse`) to each provider's console allow-list. */
  readonly googleClientId?: string;
  readonly googleClientSecret?: cdk.SecretValue;
  readonly amazonClientId?: string;
  readonly amazonClientSecret?: cdk.SecretValue;
}

export class AppSyncAuth extends Construct {
  // Shopping pool — customers, React SPA. Self-signup, social federation, Customer group.
  public readonly shoppingUserPool: cognito.UserPool;
  public readonly shoppingUserPoolClient: cognito.UserPoolClient;
  public readonly shoppingHostedUiUrl: string;

  // Management pool — staff, Blazor WASM app. Login-only: no self-signup, no social
  // federation. Accounts are provisioned manually via the AWS Console/CLI
  // (AdminCreateUser) and hold the Admin/Seller groups.
  public readonly managementUserPool: cognito.UserPool;
  public readonly managementUserPoolClient: cognito.UserPoolClient;
  public readonly managementHostedUiUrl: string;

  constructor(scope: Construct, id: string, props: AppSyncAuthProps) {
    super(scope, id);

    const passwordPolicy = {
      minLength: 8,
      requireUppercase: true,
      requireLowercase: true,
      requireDigits: true,
      requireSymbols: false,
    };

    this.shoppingUserPool = new cognito.UserPool(this, 'UserPool', {
      userPoolName: 'duckstore-users',
      selfSignUpEnabled: true,
      signInAliases: { email: true },
      autoVerify: { email: true },
      standardAttributes: {
        email: { required: true, mutable: true },
        // `fullname` maps to the OIDC `name` claim. Making it required adds a
        // "Name" field to the Managed Login sign-up form. Required standard
        // attributes can only be set at pool creation, so adding this REPLACES
        // the pool (existing users are dropped). Name still lives authoritatively
        // in the User service profile (ADR-0017); this only seeds the claim.
        fullname: { required: true, mutable: true },
      },
      passwordPolicy,
      accountRecovery: cognito.AccountRecovery.EMAIL_ONLY,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // RBAC group — checked via ctx.identity.groups in AppSync JS resolvers. Admin/Seller
    // live in the Management pool below: only staff sign in through the Blazor app, and
    // that pool is login-only (no self-signup), so those groups belong there instead.
    new cognito.CfnUserPoolGroup(this, 'CustomerGroup', {
      userPoolId: this.shoppingUserPool.userPoolId,
      groupName: 'Customer',
      description: 'Regular store customers',
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

    // Wildcarded ARN breaks the circular dependency:
    //   UserPool → Lambda trigger (LambdaConfig Fn::GetAtt)
    //   Lambda IAM policy → UserPool (Fn::GetAtt) ← removes this link
    assignCustomerGroupFn.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ['cognito-idp:AdminAddUserToGroup'],
        resources: [
          `arn:aws:cognito-idp:${cdk.Stack.of(this).region}:${cdk.Stack.of(this).account}:userpool/*`,
        ],
      }),
    );

    this.shoppingUserPool.addTrigger(
      cognito.UserPoolOperation.POST_CONFIRMATION,
      assignCustomerGroupFn,
    );

    const shoppingDomain = this.shoppingUserPool.addDomain('Domain', {
      cognitoDomain: { domainPrefix: 'duckstore' },
      // Managed Login v2 — required for the CfnManagedLoginBranding style below.
      managedLoginVersion: cognito.ManagedLoginVersion.NEWER_MANAGED_LOGIN,
    });

    // Social federation (optional, Shopping pool only — staff sign in Cognito-only).
    // Each IdP maps the provider's verified email + name onto the pool's required
    // `email`/`name` attributes so federated users satisfy the schema. The app client
    // must depend on the IdP resources, and list them in supportedIdentityProviders
    // for the buttons to render.
    const identityProviders: cognito.UserPoolClientIdentityProvider[] = [
      cognito.UserPoolClientIdentityProvider.COGNITO,
    ];
    const idpResources: Construct[] = [];

    if (props.googleClientId) {
      const google = new cognito.UserPoolIdentityProviderGoogle(this, 'GoogleIdP', {
        userPool: this.shoppingUserPool,
        clientId: props.googleClientId,
        clientSecretValue: props.googleClientSecret,
        scopes: ['openid', 'email', 'profile'],
        attributeMapping: {
          email: cognito.ProviderAttribute.GOOGLE_EMAIL,
          fullname: cognito.ProviderAttribute.GOOGLE_NAME,
        },
      });
      identityProviders.push(cognito.UserPoolClientIdentityProvider.GOOGLE);
      idpResources.push(google);
    }

    if (props.amazonClientId) {
      const amazon = new cognito.UserPoolIdentityProviderAmazon(this, 'AmazonIdP', {
        userPool: this.shoppingUserPool,
        clientId: props.amazonClientId,
        // Amazon L2 only accepts a plain string; unsafeUnwrap yields the
        // {{resolve:ssm-secure:...}} dynamic reference, resolved at deploy.
        clientSecret: props.amazonClientSecret?.unsafeUnwrap() ?? '',
        scopes: ['profile'],
        attributeMapping: {
          email: cognito.ProviderAttribute.AMAZON_EMAIL,
          fullname: cognito.ProviderAttribute.AMAZON_NAME,
        },
      });
      identityProviders.push(cognito.UserPoolClientIdentityProvider.AMAZON);
      idpResources.push(amazon);
    }

    this.shoppingUserPoolClient = this.shoppingUserPool.addClient('SpaClient', {
      userPoolClientName: 'duckstore-spa',
      generateSecret: false,
      supportedIdentityProviders: identityProviders,
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [
          cognito.OAuthScope.OPENID,
          cognito.OAuthScope.EMAIL,
          cognito.OAuthScope.PROFILE,
        ],
        callbackUrls: props.spaBaseUrls.map((url) => `${url}/api/auth/callback`),
        logoutUrls: [...props.spaBaseUrls],
      },
      idTokenValidity: cdk.Duration.hours(1),
      accessTokenValidity: cdk.Duration.hours(1),
      refreshTokenValidity: cdk.Duration.days(30),
      preventUserExistenceErrors: true,
    });
    // CloudFormation must create the IdPs before the client references them.
    idpResources.forEach((idp) => this.shoppingUserPoolClient.node.addDependency(idp));

    this.shoppingHostedUiUrl = `https://${shoppingDomain.domainName}.auth.${cdk.Stack.of(this).region}.amazoncognito.com`;

    // -------------------------------------------------------------------------------
    // Management pool — staff (Admin/Seller), Blazor WASM app. Login-only: no
    // self-signup, no social federation. Accounts are provisioned manually via the
    // AWS Console/CLI (AdminCreateUser) — group membership (Admin/Seller) is what
    // actually authorizes operations, enforced in the AppSync resolvers, not here.
    // -------------------------------------------------------------------------------
    this.managementUserPool = new cognito.UserPool(this, 'ManagementUserPool', {
      userPoolName: 'duckstore-management-users',
      selfSignUpEnabled: false,
      signInAliases: { email: true },
      standardAttributes: {
        email: { required: true, mutable: true },
      },
      passwordPolicy,
      accountRecovery: cognito.AccountRecovery.EMAIL_ONLY,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    new cognito.CfnUserPoolGroup(this, 'SellerGroup', {
      userPoolId: this.managementUserPool.userPoolId,
      groupName: 'Seller',
      description: 'Sellers who manage the catalog',
    });
    new cognito.CfnUserPoolGroup(this, 'AdminGroup', {
      userPoolId: this.managementUserPool.userPoolId,
      groupName: 'Admin',
      description: 'Admins with full access to orders and management',
    });

    const managementDomain = this.managementUserPool.addDomain('ManagementDomain', {
      cognitoDomain: { domainPrefix: 'duckstore-management' },
      managedLoginVersion: cognito.ManagedLoginVersion.NEWER_MANAGED_LOGIN,
    });

    // Public PKCE client like the Shopping SPA's, but Cognito-only (no social sign-in
    // for staff) and with the Blazor OIDC library's fixed callback paths.
    this.managementUserPoolClient = this.managementUserPool.addClient('ManagementClient', {
      userPoolClientName: 'duckstore-management',
      generateSecret: false,
      supportedIdentityProviders: [cognito.UserPoolClientIdentityProvider.COGNITO],
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [
          cognito.OAuthScope.OPENID,
          cognito.OAuthScope.EMAIL,
          cognito.OAuthScope.PROFILE,
        ],
        callbackUrls: props.managementBaseUrls.map(
          (url) => `${url}/authentication/login-callback`,
        ),
        logoutUrls: props.managementBaseUrls.map(
          (url) => `${url}/authentication/logout-callback`,
        ),
      },
      idTokenValidity: cdk.Duration.hours(1),
      accessTokenValidity: cdk.Duration.hours(1),
      refreshTokenValidity: cdk.Duration.days(30),
      preventUserExistenceErrors: true,
    });

    this.managementHostedUiUrl = `https://${managementDomain.domainName}.auth.${cdk.Stack.of(this).region}.amazoncognito.com`;

    // DuckStore branding for the Managed Login pages, so the hosted sign-in/sign-up
    // stops looking white-label. Colors are the app's amber `--primary` theme
    // (app/globals.css, oklch → sRGB) in Cognito's RGBA 8-digit hex format.
    // Cognito validates `settings` strictly — an unknown key under `components` or
    // `componentClasses` fails the deploy with UnknownProperty; unspecified keys
    // keep Cognito defaults.
    //
    // Managed Login v2 (enabled on both domains above) requires EVERY app client to
    // have its own branding resource, or its /login page 404s with "Login pages
    // unavailable — please contact an administrator" — there is no pool-wide default.
    // Both clients share this same style.
    const managedLoginSettings = {
      components: {
        primaryButton: {
          lightMode: {
            defaults: { backgroundColor: 'bc8500ff', textColor: '181000ff' },
            hover: { backgroundColor: 'a67400ff', textColor: '181000ff' },
            active: { backgroundColor: '8f6400ff', textColor: '181000ff' },
          },
          darkMode: {
            defaults: { backgroundColor: 'ffd12eff', textColor: '181000ff' },
            hover: { backgroundColor: 'e6b800ff', textColor: '181000ff' },
            active: { backgroundColor: 'cca300ff', textColor: '181000ff' },
          },
        },
        // Solid site background (--background) instead of Cognito's default
        // purple/pink gradient image.
        pageBackground: {
          image: { enabled: false },
          lightMode: { color: 'f8f8faff' },
          darkMode: { color: '0f1b2aff' },
        },
        // Show the DuckStore duck on the form card (asset uploaded below) —
        // it's disabled by Cognito default, which is why it wasn't rendering.
        form: { logo: { enabled: true } },
        // Branded header bar with the logo so the page isn't an empty expanse.
        pageHeader: { logo: { enabled: true } },
      },
      componentClasses: {
        // Links (Create an account / Forgot your password) — amber, not blue.
        link: {
          lightMode: { defaults: { textColor: 'bc8500ff' }, hover: { textColor: '8f6400ff' } },
          darkMode: { defaults: { textColor: 'ffd12eff' }, hover: { textColor: 'e6b800ff' } },
        },
        // Input focus ring — amber (matches --ring), not blue.
        focusState: {
          lightMode: { borderColor: 'bc8500ff' },
          darkMode: { borderColor: 'ffd12eff' },
        },
      },
    };

    // The DuckStore duck logo (src/.../public/icon.svg), base64-inlined so this
    // construct stays self-contained when the SPA moves to its own submodule.
    // Placed on both the form card and the page header, for light + dark.
    const managedLoginAssets = (['FORM_LOGO', 'PAGE_HEADER_LOGO'] as const).flatMap((category) =>
      (['LIGHT', 'DARK'] as const).map((colorMode) => ({
        category,
        colorMode,
        extension: 'SVG',
        bytes: DUCK_LOGO_SVG_BASE64,
      })),
    );

    new cognito.CfnManagedLoginBranding(this, 'SpaBranding', {
      userPoolId: this.shoppingUserPool.userPoolId,
      clientId: this.shoppingUserPoolClient.userPoolClientId,
      useCognitoProvidedValues: false,
      settings: managedLoginSettings,
      assets: managedLoginAssets,
    }).node.addDependency(shoppingDomain);

    new cognito.CfnManagedLoginBranding(this, 'ManagementBranding', {
      userPoolId: this.managementUserPool.userPoolId,
      clientId: this.managementUserPoolClient.userPoolClientId,
      useCognitoProvidedValues: false,
      settings: managedLoginSettings,
      assets: managedLoginAssets,
    }).node.addDependency(managementDomain);
  }
}

// Base64-encoded DuckStore duck logo (public/icon.svg). Inlined to avoid a
// cross-package file read once the React SPA becomes a git submodule.
const DUCK_LOGO_SVG_BASE64 =
  'PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAxMDAgMTAwIj4KICA8cmVjdCB3aWR0aD0iMTAwIiBoZWlnaHQ9IjEwMCIgcng9IjIwIiBmaWxsPSIjMWExYTJlIi8+CiAgPCEtLSBCb2R5IC0tPgogIDxlbGxpcHNlIGN4PSI0NCIgY3k9IjcwIiByeD0iMzAiIHJ5PSIyMSIgZmlsbD0iI0ZGRDcwMCIvPgogIDwhLS0gSGVhZCAtLT4KICA8Y2lyY2xlIGN4PSI2NCIgY3k9IjQzIiByPSIxOSIgZmlsbD0iI0ZGRDcwMCIvPgogIDwhLS0gQmVhayAtLT4KICA8cG9seWdvbiBwb2ludHM9IjgwLDQxIDk2LDM2IDk2LDQ4IiBmaWxsPSIjRkY4QzAwIi8+CiAgPCEtLSBFeWUgLS0+CiAgPGNpcmNsZSBjeD0iNjkiIGN5PSIzNyIgcj0iNCIgZmlsbD0iIzFhMWExYSIvPgogIDxjaXJjbGUgY3g9IjcwLjUiIGN5PSIzNS41IiByPSIxLjIiIGZpbGw9IndoaXRlIi8+CiAgPCEtLSBXaW5nIGRldGFpbCAtLT4KICA8cGF0aCBkPSJNMjQgNjQgUTQwIDU0IDU3IDY0IiBzdHJva2U9IiNFNkJFMDAiIHN0cm9rZS13aWR0aD0iMi41IiBmaWxsPSJub25lIiBzdHJva2UtbGluZWNhcD0icm91bmQiLz4KPC9zdmc+Cg==';
