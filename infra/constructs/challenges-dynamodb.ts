import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class ChallengesDynamoDB extends Construct {
  public readonly challengesTable: dynamodb.Table;
  public readonly challengeProgressTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // PK=QuestionId, SK=PUBLIC|ANSWER (ADR-0045 §2). GSI1 is sparse by construction: only the
    // PUBLIC item carries GSI1PK/GSI1SK, so the ANSWER item (correct option, explanation, hints)
    // is never reachable through the browse/search GSI — leaking it would be a schema change,
    // not a review-time slip. No stream: reads are all direct (repository GetItem/AppSync direct
    // resolvers), nothing downstream needs to react to a question being authored.
    this.challengesTable = new dynamodb.Table(this, 'ChallengesTable', {
      tableName: 'challenges',
      partitionKey: { name: 'QuestionId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'SK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.challengesTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'GSI1PK', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'GSI1SK', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    // PK=OwnerId, SK=PROFILE|ATTEMPT#<questionId>|REDEMPTION#<id> (ADR-0045 §5, REDEMPTION# from
    // CH-11). No GSI — myChallengeProgress and every write are scoped to a single OwnerId
    // partition. NEW_AND_OLD_IMAGES drives challenges-progress-stream-publisher (ADR-0045 §7): the
    // rule dispatcher diffs Old/New to detect the IsCorrect null→set transition (an attempt row can
    // be pre-created by a hint reveal before it's ever answered) and the REDEMPTION# insert
    // (ADR-0046 §3).
    this.challengeProgressTable = new dynamodb.Table(this, 'ChallengeProgressTable', {
      tableName: 'challenge-progress',
      partitionKey: { name: 'OwnerId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'SK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
