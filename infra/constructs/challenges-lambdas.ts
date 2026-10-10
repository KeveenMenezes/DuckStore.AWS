import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as events from 'aws-cdk-lib/aws-events';
import * as targets from 'aws-cdk-lib/aws-events-targets';
import * as destinations from 'aws-cdk-lib/aws-lambda-destinations';
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
  readonly pointsTransactionsTable: dynamodb.Table;
}

export class ChallengesLambdas extends Construct {
  public readonly submitChallengeAnswer: lambda.Function;
  public readonly revealChallengeHint: lambda.Function;
  public readonly redeemChallengePoints: lambda.Function;
  public readonly challengesProgressStreamPublisher: lambda.Function;
  public readonly reviewCreatedConsumer: lambda.Function;
  public readonly basketCheckoutConsumer: lambda.Function;
  public readonly pointsTransactionsStreamPublisher: lambda.Function;
  public readonly paymentAuthorizedConsumer: lambda.Function;
  public readonly orderCancelledConsumer: lambda.Function;

  constructor(scope: Construct, id: string, props: ChallengesLambdasProps) {
    super(scope, id);

    const { challengesTable, challengeProgressTable, pointsTransactionsTable } = props;

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
    // Write-only: a correct answer Puts its CHALLENGE# credit in the same TransactWriteItems as
    // the attempt and the balance (ADR-0048 §2). The Lambda never reads the ledger back.
    pointsTransactionsTable.grantWriteData(this.submitChallengeAnswer);

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

    // 5. challenges-review-created-consumer
    //    Trigger: EventBridge rule (ReviewCreatedEvent, source=duckstore) — the second rule on
    //    this event, next to CatalogView's. Credits the review points once per product: one
    //    TransactWriteItems Puts the REVIEW#<productId> ledger row (attribute_not_exists) and ADDs
    //    to PROFILE Score (SPEC-review-points, ADR-0048 §2). Publishes nothing and reads nothing,
    //    so it only needs those two item actions.
    this.reviewCreatedConsumer = new lambda.Function(this, 'ReviewCreatedConsumer', {
      functionName: 'challenges-review-created-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Consumes ReviewCreatedEvent and credits the review points once per product',
      environment: { ANNOTATIONS_HANDLER: 'ReviewCreatedConsumer' },
    });
    pointsTransactionsTable.grant(this.reviewCreatedConsumer, 'dynamodb:PutItem');
    challengeProgressTable.grant(this.reviewCreatedConsumer, 'dynamodb:UpdateItem');

    const reviewCreatedRule = new events.Rule(this, 'ReviewCreatedRule', {
      eventBus,
      ruleName: 'challenges-review-created-consumer-rule',
      description:
        'Routes ReviewCreatedEvent (source=duckstore) to challenges-review-created-consumer',
      eventPattern: {
        source: ['duckstore'],
        detailType: ['ReviewCreatedEvent'],
      },
    });
    this.reviewCreatedConsumer.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    reviewCreatedRule.addTarget(
      new targets.LambdaFunction(this.reviewCreatedConsumer, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );

    // 6. challenges-basket-checkout-consumer
    //    Trigger: EventBridge rule (BasketCheckoutEvent, source=duckstore). Reserves the points a
    //    checkout used (ADR-0048 §5): one TransactWriteItems debits PROFILE under Score >= points
    //    and Puts REDEMPTION#<orderId> Reserved; a short balance Puts a Failed row instead.
    //    Checkouts without points are ignored. Writes only those two item actions.
    this.basketCheckoutConsumer = new lambda.Function(this, 'BasketCheckoutConsumer', {
      functionName: 'challenges-basket-checkout-consumer',
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description: 'Consumes BasketCheckoutEvent and reserves the points the checkout used',
      environment: { ANNOTATIONS_HANDLER: 'BasketCheckoutConsumer' },
    });
    pointsTransactionsTable.grant(this.basketCheckoutConsumer, 'dynamodb:PutItem');
    challengeProgressTable.grant(this.basketCheckoutConsumer, 'dynamodb:UpdateItem');
    this.addEventConsumer(
      this.basketCheckoutConsumer, eventBus, dlq, 'BasketCheckoutRule',
      'challenges-basket-checkout-consumer', 'BasketCheckoutEvent',
    );

    // 7. challenges-points-transactions-stream-publisher
    //    Trigger: DynamoDB Streams on points-transactions (NEW_AND_OLD_IMAGES). Rule-based
    //    dispatcher (ADR-0019): a redemption row's INSERT → PointsReservedEvent (Reserved) or
    //    PointsReservationFailedEvent (Failed), which Payment waits on (ADR-0048 §5). Credits and
    //    settlement MODIFYs publish nothing.
    this.pointsTransactionsStreamPublisher = new lambda.Function(
      this,
      'PointsTransactionsStreamPublisher',
      {
        functionName: 'challenges-points-transactions-stream-publisher',
        tracing: lambda.Tracing.ACTIVE,
        architecture: DOTNET_ARCH,
        runtime: DOTNET_RUNTIME,
        // provided.al2023 runs the file named `bootstrap`; this value is inert.
        handler: 'bootstrap',
        code: challengesCode,
        timeout: cdk.Duration.seconds(30),
        memorySize: DOTNET_MEMORY_MB,
        description:
          'CDC: reads DynamoDB Streams on points-transactions and publishes PointsReserved/PointsReservationFailed to EventBridge',
        environment: {
          ANNOTATIONS_HANDLER: 'ChallengesPointsTransactionsStreamPublisher',
          EventBridge__BusName: eventBus.eventBusName,
        },
      },
    );

    this.pointsTransactionsStreamPublisher.addEventSource(
      new lambdaEventSources.DynamoEventSource(pointsTransactionsTable, {
        startingPosition: lambda.StartingPosition.TRIM_HORIZON,
        batchSize: 10,
        bisectBatchOnError: true,
        retryAttempts: 3,
        // Records exhausted after bisect+retries land here (ADR-0015 §1) — shard/sequence
        // metadata, not the payload; redrive by re-reading the stream.
        onFailure: new lambdaEventSources.SqsDlq(dlq.queue),
      }),
    );

    eventBus.grantPutEventsTo(this.pointsTransactionsStreamPublisher);

    // 8–9. Settlement of the checkout's points (ADR-0048 §5). Every authorized payment / cancelled
    //    order reaches them; the REDEMPTION# row is read by its base-table key (the events carry
    //    the customer) with a consistent read, so an order without points is a plain no-op.
    //    Every status write is conditional on the status it leaves, which makes redeliveries
    //    no-ops too.
    //    - challenges-payment-authorized-consumer: Reserved → Used.
    //    - challenges-order-cancelled-consumer: Reserved → Released / Used → Refunded, giving the
    //      points back to PROFILE Score in the same transaction.
    this.paymentAuthorizedConsumer = this.settlementConsumer(
      'PaymentAuthorizedConsumer', 'challenges-payment-authorized-consumer', 'PaymentAuthorizedEvent',
      'Consumes PaymentAuthorizedEvent and marks the order\'s reserved points Used',
      { challengesCode, eventBus, dlq, challengeProgressTable, pointsTransactionsTable },
    );
    this.orderCancelledConsumer = this.settlementConsumer(
      'OrderCancelledConsumer', 'challenges-order-cancelled-consumer', 'OrderCancelledEvent',
      'Consumes OrderCancelledEvent and gives the order\'s points back (Released/Refunded)',
      { challengesCode, eventBus, dlq, challengeProgressTable, pointsTransactionsTable },
    );
  }

  private settlementConsumer(
    id: string,
    functionName: string,
    detailType: string,
    description: string,
    deps: {
      challengesCode: lambda.Code;
      eventBus: events.IEventBus;
      dlq: ContextDlq;
      challengeProgressTable: dynamodb.ITable;
      pointsTransactionsTable: dynamodb.ITable;
    },
  ): lambda.Function {
    const fn = new lambda.Function(this, id, {
      functionName,
      tracing: lambda.Tracing.ACTIVE,
      architecture: DOTNET_ARCH,
      runtime: DOTNET_RUNTIME,
      // provided.al2023 runs the file named `bootstrap`; this value is inert.
      handler: 'bootstrap',
      code: deps.challengesCode,
      timeout: cdk.Duration.seconds(30),
      memorySize: DOTNET_MEMORY_MB,
      description,
      environment: { ANNOTATIONS_HANDLER: id },
    });
    deps.pointsTransactionsTable.grant(fn, 'dynamodb:GetItem', 'dynamodb:UpdateItem');
    // Only OrderCancelled returns points, but both share the shape; ADD on PROFILE is all it does.
    deps.challengeProgressTable.grant(fn, 'dynamodb:UpdateItem');
    this.addEventConsumer(fn, deps.eventBus, deps.dlq, `${id}Rule`, functionName, detailType);
    return fn;
  }

  // EventBridge rule → consumer Lambda, with the context DLQ on both legs: the rule's target
  // delivery and the Lambda's own async retries (same shape as every consumer above).
  private addEventConsumer(
    fn: lambda.Function,
    eventBus: events.IEventBus,
    dlq: ContextDlq,
    ruleId: string,
    functionName: string,
    detailType: string,
  ) {
    const rule = new events.Rule(this, ruleId, {
      eventBus,
      ruleName: `${functionName}-rule`,
      description: `Routes ${detailType} (source=duckstore) to ${functionName}`,
      eventPattern: {
        source: ['duckstore'],
        detailType: [detailType],
      },
    });
    fn.configureAsyncInvoke({
      onFailure: new destinations.SqsDestination(dlq.queue),
      retryAttempts: 2,
    });
    rule.addTarget(
      new targets.LambdaFunction(fn, {
        deadLetterQueue: dlq.queue,
        retryAttempts: 3,
        maxEventAge: cdk.Duration.hours(2),
      }),
    );
  }
}
