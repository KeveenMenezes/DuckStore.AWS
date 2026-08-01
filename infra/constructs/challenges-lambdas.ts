import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import { Construct } from 'constructs';
import { ContextDlq } from './context-dlq';
import {
  DOTNET_ARCH,
  DOTNET_MEMORY_MB,
  DOTNET_RUNTIME,
  dotnetLambdaCode,
} from './dotnet-lambda-code';

export interface ChallengesLambdasProps {
  readonly challengesTable: dynamodb.Table;
  readonly challengeProgressTable: dynamodb.Table;
}

export class ChallengesLambdas extends Construct {
  public readonly submitChallengeAnswer: lambda.Function;
  public readonly revealChallengeHint: lambda.Function;
  public readonly redeemChallengePoints: lambda.Function;
  public readonly challengesProgressStreamPublisher: lambda.Function;

  constructor(scope: Construct, id: string, props: ChallengesLambdasProps) {
    super(scope, id);

    const { challengesTable, challengeProgressTable } = props;

    // EventBridge bus — created by FoundationStack; imported here by name (ADR-0004).
    const eventBus = events.EventBus.fromEventBusName(this, 'EventBus', 'duckstore-event-bus');

    // Shared dead-letter queue for async Challenges processing; a non-empty queue
    // trips the challenges-dlq-not-empty alarm → duckstore-alerts.
    const dlq = new ContextDlq(this, 'Dlq', { contextName: 'challenges' });

    // One ZIP per service, shared by all its functions; each Lambda selects its
    // handler through ANNOTATIONS_HANDLER instead of a Docker cmd override (ADR-0042).
    const challengesCode = dotnetLambdaCode(
      'src/Services/Challenges',
      'src/Services/Challenges/Challenges.Function/Challenges.Function.csproj',
    );

    // 1. challenges-submit-answer  (AppSync Invoke — Mutation.submitChallengeAnswer)
    //    Grades server-side against the ANSWER item and finalizes the attempt transactionally —
    //    the answer key never reaches AppSync/the browser (ADR-0045 §4/§8).
    this.submitChallengeAnswer = new lambda.Function(this, 'SubmitChallengeAnswer', {
      functionName: 'challenges-submit-answer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Grades a submitted challenge answer server-side and records the attempt',
      environment: { ANNOTATIONS_HANDLER: 'SubmitChallengeAnswer' },
    });
    challengesTable.grantReadData(this.submitChallengeAnswer);
    challengeProgressTable.grantReadWriteData(this.submitChallengeAnswer);

    // 2. challenges-reveal-hint  (AppSync Invoke — Mutation.revealChallengeHint)
    //    Commits the hint penalty before returning the hint text (ADR-0045 §6) — the read of the
    //    hint itself never happens if the conditional write loses the race or the cap is hit.
    this.revealChallengeHint = new lambda.Function(this, 'RevealChallengeHint', {
      functionName: 'challenges-reveal-hint',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Commits the hint-reveal penalty and returns the next hint for a challenge',
      environment: { ANNOTATIONS_HANDLER: 'RevealChallengeHint' },
    });
    challengesTable.grantReadData(this.revealChallengeHint);
    challengeProgressTable.grantReadWriteData(this.revealChallengeHint);

    // 3. challenges-redeem-points  (AppSync Invoke — Mutation.redeemChallengePoints)
    //    Debits the balance via a conditional TransactWriteItems (Score >= points) and writes the
    //    REDEMPTION# ledger row in the same transaction (ADR-0046 §2). No currency amount ever
    //    passes through this Lambda — Pricing converts points to money asynchronously off the
    //    PointsRedeemedEvent the ledger write triggers (ADR-0046 §1, §4).
    this.redeemChallengePoints = new lambda.Function(this, 'RedeemChallengePoints', {
      functionName: 'challenges-redeem-points',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Debits a player\'s point balance and writes the redemption ledger row',
      environment: { ANNOTATIONS_HANDLER: 'RedeemChallengePoints' },
    });
    challengeProgressTable.grantReadWriteData(this.redeemChallengePoints);

    // 4. challenges-progress-stream-publisher
    //    Trigger: DynamoDB Streams on challenge-progress (NEW_AND_OLD_IMAGES). Rule-based
    //    dispatcher (ADR-0019): the IsCorrect null→set transition → ChallengeAnsweredEvent, a
    //    REDEMPTION# insert → PointsRedeemedEvent (ADR-0045 §7, ADR-0046 §3). MODIFY of PROFILE
    //    never matches either rule, so the KPI-fold write publishes nothing.
    this.challengesProgressStreamPublisher = new lambda.Function(
      this,
      'ChallengesProgressStreamPublisher',
      {
        functionName: 'challenges-progress-stream-publisher',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
        // provided.al2023 runs the file named `bootstrap`; this value is inert.
        handler: 'bootstrap',
        code: challengesCode,
        timeout: cdk.Duration.seconds(30),
        memorySize: DOTNET_MEMORY_MB,
        description:
          'CDC: reads DynamoDB Streams on challenge-progress and publishes ChallengeAnswered/PointsRedeemed to EventBridge',
        environment: {
          ANNOTATIONS_HANDLER: 'ChallengesProgressStreamPublisher',
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    this.challengesProgressStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(challengeProgressTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (shard/sequence
        // metadata, not the payload — redrive by re-reading the stream).
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.challengesProgressStreamPublisher);
  }
}
