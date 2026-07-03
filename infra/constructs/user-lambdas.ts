import * as path from 'path';
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets';
import { Construct } from 'constructs';

const DOTNET_ARCH = lambda.Architecture.ARM_64;

const REPO_ROOT = path.join(__dirname, '..', '..');
const USER_DOCKERFILE = 'src/Services/User/User.Function/Dockerfile';

export interface UserLambdasProps {
  readonly userProfilesTable: dynamodb.Table;
}

export class UserLambdas extends Construct {
  public readonly getProfile: lambda.Function;

  constructor(scope: Construct, id: string, props: UserLambdasProps) {
    super(scope, id);

    const { userProfilesTable } = props;

    const userImage = new ecrAssets.DockerImageAsset(this, 'UserImage', {
      directory: REPO_ROOT,
      file: USER_DOCKERFILE,
      platform: ecrAssets.Platform.LINUX_ARM64,
      exclude: [
        '**',
        '!Directory.Packages.props',
        '!nuget.config',
        '!src/Services/User/User.Function/**',
        '!src/BuildingBlocks/**',
      ],
    });
    const userCode = (cmd: string[]) =>
      lambda.DockerImageCode.fromEcr(userImage.repository, {
        tagOrDigest: userImage.imageTag,
        cmd,
      });

    // -------------------------------------------------------------------------
    // user-get-profile  (invoked by the AppSync `myProfile` Lambda resolver)
    //   Lazy provisioning (ADR-0017): GetItem by UserId; if absent, seed from the
    //   Cognito claims passed in the payload and PutItem; return the profile.
    // -------------------------------------------------------------------------
    this.getProfile = new lambda.DockerImageFunction(this, 'GetProfile', {
      functionName: 'user-get-profile',
      architecture: DOTNET_ARCH,
      code: userCode([
        'User.Function::User.Function.Functions_GetProfile_Generated::GetProfile',
      ]),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      description: 'Gets or lazily provisions the authenticated user profile in DynamoDB',
    });

    userProfilesTable.grantReadWriteData(this.getProfile);
  }
}
