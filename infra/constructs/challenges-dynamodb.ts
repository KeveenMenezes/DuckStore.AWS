import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class ChallengesDynamoDB extends Construct {
  public readonly challengesTable: dynamodb.Table;
  public readonly challengeProgressTable: dynamodb.Table;
  public readonly pointsTransactionsTable: dynamodb.Table;

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

    // The points ledger (ADR-0048 §1): one row per balance change. PK=OwnerId,
    // SK=TransactionId — deterministic per source (CHALLENGE#<questionId>, REVIEW#<productId>,
    // REDEMPTION#<orderId>) and written with attribute_not_exists, which is the idempotency key.
    // LSI1 (OwnerId + CreatedAt) orders myPointsHistory by date; it can only be declared at table
    // creation, so it ships with the table. GSI1 is sparse on OrderId (redemption rows only) so
    // payment/order events can find their row. No TTL — the ledger is permanent.
    // NEW_AND_OLD_IMAGES drives challenges-points-transactions-stream-publisher (ADR-0048 §6).
    this.pointsTransactionsTable = new dynamodb.Table(this, 'PointsTransactionsTable', {
      tableName: 'points-transactions',
      partitionKey: { name: 'OwnerId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'TransactionId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.pointsTransactionsTable.addLocalSecondaryIndex({
      indexName: 'LSI1',
      sortKey: { name: 'CreatedAt', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });

    this.pointsTransactionsTable.addGlobalSecondaryIndex({
      indexName: 'GSI1',
      partitionKey: { name: 'OrderId', type: dynamodb.AttributeType.STRING },
      projectionType: dynamodb.ProjectionType.ALL,
    });
  }
}
