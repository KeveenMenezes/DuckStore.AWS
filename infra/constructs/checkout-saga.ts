import * as cdk from 'aws-cdk-lib';
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as cloudwatchActions from 'aws-cdk-lib/aws-cloudwatch-actions';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as sfn from 'aws-cdk-lib/aws-stepfunctions';
import { Construct } from 'constructs';
import { importAlertsTopic } from './context-dlq';

// Step Functions Express saga behind the checkoutBasket mutation (ADR-0050), started synchronously
// by AppSync through the same HTTP data source as product-create-saga (ADR-0032):
//
//   ReadInput → DeriveOrderId → LoadCheckoutRecord ─(exists)→ replay
//     → LoadCart → CatalogLines → Quote → RegisterPayment → Checkout ─(CartChanged)→ VoidPayment
//
// Written as JSONata ASL rather than CDK state constructs: the expressions are the substance of
// this saga, and keeping them as plain ASL keeps them reviewable in one place and testable with
// `aws stepfunctions test-state`.
//
// Outcomes (ADR-0050 §8):
//   - success          → SUCCEEDED { isSuccess: true, orderId }
//   - typed refusal    → SUCCEEDED { refusal: { errorType, message, errorInfo } }
//   - anything else    → FAILED InternalError / CompensationFailed (alarmed)
// Refusals are expected outcomes, so they never count as failed executions; ExecutionsFailed then
// means exactly "something broke" and is alarmed directly.
//
// Input contract (built by Mutation.checkoutBasket.js from the Cognito token and CheckoutInput):
//   { ownerId: "USER#<sub>", customerId: "<sub>", checkoutId: string, input: CheckoutInput }
// The card travels in `input` as far as RegisterPayment; it never reaches the output, and the logs
// carry no execution data (ADR-0050 §7).
//
// Timeout budget (ADR-0050 §1): each Lambda task allows 2 attempts of 2 s with a 1 s interval (5 s),
// each DynamoDB read one 1 s attempt (3 s for the three reads). Slowest paths: every Lambda retried
// and failing = 3 + 3 × 5 = 18 s; a Checkout that times out once and then returns CartChanged,
// followed by a fully retried void = 3 + 5 + 5 + 5 + 5 = 23 s — both under the 25 s execution
// timeout, itself under AppSync's 30 s, so every failure lands in a Catch.

const LAMBDA_TIMEOUT_SECONDS = 2;
const DYNAMODB_TIMEOUT_SECONDS = 1;
const EXECUTION_TIMEOUT_SECONDS = 25;

// Errors that mean "the call didn't happen or failed before the function ran" — safe to retry on
// any step. Function errors (an exception thrown by the handler) are not in this list: they fall
// through to the Catch as an ambiguous failure.
const LAMBDA_TRANSIENT_ERRORS = [
  'Lambda.ServiceException',
  'Lambda.AWSLambdaException',
  'Lambda.SdkClientException',
  'Lambda.TooManyRequestsException',
  'States.Timeout',
];

// Basket's close is idempotent per checkoutId, so a transaction conflict thrown out of it is safe
// to retry too (the .NET runtime reports the exception's type name as the error).
const CHECKOUT_RETRYABLE_ERRORS = [...LAMBDA_TRANSIENT_ERRORS, 'TransactionCanceledException'];

const lambdaRetry = (errors: string[]) => [
  { ErrorEquals: errors, IntervalSeconds: 1, MaxAttempts: 1, BackoffRate: 2 },
];

const toInternalError = [{ ErrorEquals: ['States.ALL'], Next: 'InternalError' }];

// JSONata collapses one-element sequences into the element itself; appending to [] restores the
// array where an API (BatchGetItem Keys, a Lambda's list field) requires one.
const asArray = (expression: string) => `$append([], ${expression})`;

// A top-level Arguments field whose expression yields nothing fails the state
// (States.QueryEvaluationError), so a field the client may omit maps to an explicit null.
const optional = (path: string) => `{% $exists(${path}) ? ${path} : null %}`;

// Refusal outputs, mapped by the resolver to errorType/errorInfo (ADR-0050 §8).
const refusal = (errorType: string, message: string, errorInfo = '{}') => ({
  Type: 'Succeed',
  Output: `{% { "refusal": { "errorType": ${errorType}, "message": ${message}, "errorInfo": ${errorInfo} } } %}`,
});

export interface CheckoutSagaProps {
  readonly quoteFunctionName: string;
  readonly registerPaymentFunctionName: string;
  readonly voidPaymentFunctionName: string;
  readonly checkoutFunctionName: string;
}

export class CheckoutSaga extends Construct {
  public readonly stateMachine: sfn.StateMachine;

  constructor(scope: Construct, id: string, props: CheckoutSagaProps) {
    super(scope, id);

    // Imported by fixed name, never by CloudFormation export (see infra/bin/app.ts).
    const cartsTable = dynamodb.Table.fromTableName(this, 'ShoppingCartsTable', 'shopping-carts');
    const productsTable = dynamodb.Table.fromTableName(this, 'ProductsTable', 'products');
    const quote = lambda.Function.fromFunctionName(this, 'QuoteFn', props.quoteFunctionName);
    const registerPayment = lambda.Function.fromFunctionName(this, 'RegisterPaymentFn', props.registerPaymentFunctionName);
    const voidPayment = lambda.Function.fromFunctionName(this, 'VoidPaymentFn', props.voidPaymentFunctionName);
    const checkout = lambda.Function.fromFunctionName(this, 'CheckoutFn', props.checkoutFunctionName);

    const definition = {
      Comment: 'checkoutBasket — ADR-0050',
      QueryLanguage: 'JSONata',
      TimeoutSeconds: EXECUTION_TIMEOUT_SECONDS,
      StartAt: 'ReadInput',
      States: {
        ReadInput: {
          Type: 'Pass',
          Assign: {
            ownerId: '{% $states.input.ownerId %}',
            customerId: '{% $states.input.customerId %}',
            checkoutId: '{% $states.input.checkoutId %}',
            checkoutInput: '{% $states.input.input %}',
          },
          Next: 'DeriveOrderId',
        },

        // OrderId = GUID(first 32 hex of SHA-256("<ownerId>#<checkoutId>")) — ADR-0050 §3, whose test
        // vectors this expression must reproduce. The owner is part of the input, so a client-chosen
        // checkoutId can never land on another customer's order.
        DeriveOrderId: {
          Type: 'Pass',
          Assign: {
            orderId:
              "{% ($h := $lowercase($hash($ownerId & '#' & $checkoutId, 'SHA-256')); " +
              "$substring($h, 0, 8) & '-' & $substring($h, 8, 4) & '-' & $substring($h, 12, 4) & '-' & " +
              "$substring($h, 16, 4) & '-' & $substring($h, 20, 12)) %}",
          },
          Next: 'LoadCheckoutRecord',
        },

        // Replay first: once this attempt closed the cart, the cart is gone, and only the CHECKOUT#
        // record still answers with the order it created.
        LoadCheckoutRecord: {
          Type: 'Task',
          Resource: 'arn:aws:states:::dynamodb:getItem',
          TimeoutSeconds: DYNAMODB_TIMEOUT_SECONDS,
          Arguments: {
            TableName: cartsTable.tableName,
            Key: { OwnerId: { S: "{% 'CHECKOUT#' & $ownerId & '#' & $checkoutId %}" } },
            ConsistentRead: true,
          },
          Output: '{% $states.result %}',
          Catch: toInternalError,
          Next: 'IsReplay',
        },
        IsReplay: {
          Type: 'Choice',
          Choices: [{ Condition: '{% $exists($states.input.Item) %}', Next: 'Replayed' }],
          Default: 'LoadCart',
        },
        Replayed: {
          Type: 'Succeed',
          Output: '{% { "isSuccess": true, "orderId": $states.input.Item.OrderId.S } %}',
        },

        LoadCart: {
          Type: 'Task',
          Resource: 'arn:aws:states:::dynamodb:getItem',
          TimeoutSeconds: DYNAMODB_TIMEOUT_SECONDS,
          Arguments: {
            TableName: cartsTable.tableName,
            Key: { OwnerId: { S: '{% $ownerId %}' } },
            ConsistentRead: true,
          },
          // Only ProductId and Quantity are taken from the client's cart (ADR-0050 §5).
          Assign: {
            cartItems:
              '{% $exists($states.result.Item) ? ' +
              asArray(
                '$map($parse($states.result.Item.Data.S).Items, function($i) { ' +
                  '{ "productId": $lowercase($string($i.ProductId)), "quantity": $i.Quantity } })',
              ) +
              ' : [] %}',
            cartVersion: '{% $exists($states.result.Item.Version) ? $states.result.Item.Version.S : null %}',
          },
          Catch: toInternalError,
          Next: 'HasItems',
        },
        HasItems: {
          Type: 'Choice',
          Choices: [
            { Condition: '{% $count($cartItems) = 0 %}', Next: 'RefuseEmptyCart' },
            // BatchGetItem takes at most 100 keys.
            { Condition: '{% $count($distinct($cartItems.productId)) > 100 %}', Next: 'RefuseCartTooLarge' },
          ],
          Default: 'CatalogLines',
        },
        RefuseEmptyCart: refusal('"BadRequest"', '"There is no cart to check out."'),
        RefuseCartTooLarge: refusal('"BadRequest"', '"A cart can hold at most 100 different products."'),

        // Name and main image come from Catalog, never from the cart (ADR-0050 §5). The AWS SDK
        // integration spells DynamoDB's multi-letter types the SDK v2 way — a boolean is `Bool`, not the
        // API's `BOOL` (single-letter S, N, L, M are unchanged) — so IsMain is read as `Bool`.
        CatalogLines: {
          Type: 'Task',
          Resource: 'arn:aws:states:::aws-sdk:dynamodb:batchGetItem',
          TimeoutSeconds: DYNAMODB_TIMEOUT_SECONDS,
          Arguments: {
            RequestItems: {
              [productsTable.tableName]: {
                Keys: `{% ${asArray('$map($distinct($cartItems.productId), function($id) { { "Id": { "S": $id } } })')} %}`,
                ProjectionExpression: 'Id, #name, Images',
                ExpressionAttributeNames: { '#name': 'Name' },
              },
            },
          },
          Assign: {
            catalog:
              `{% $merge(${asArray(
                `$map(${asArray(`$lookup($states.result.Responses, '${productsTable.tableName}')`)}, function($p) { ` +
                  '{ $p.Id.S: { "name": $p.Name.S, "imageId": $p.Images.L[M.IsMain.Bool = true][0].M.ImageId.S } } })',
              )}) %}`,
            unprocessedKeys: '{% $count($keys($states.result.UnprocessedKeys)) %}',
          },
          Catch: toInternalError,
          Next: 'CatalogComplete',
        },
        CatalogComplete: {
          Type: 'Choice',
          Choices: [
            // Throttled keys would otherwise read as deleted products.
            { Condition: '{% $unprocessedKeys > 0 %}', Next: 'InternalError' },
            {
              Condition: '{% $count($filter($cartItems.productId, function($id) { $not($exists($lookup($catalog, $id))) })) > 0 %}',
              Next: 'RefuseProductUnavailable',
            },
          ],
          Default: 'Quote',
        },
        RefuseProductUnavailable: refusal(
          '"ProductUnavailable"',
          '"Some products in your cart are no longer available."',
          `{ "productIds": ${asArray('$distinct($filter($cartItems.productId, function($id) { $not($exists($lookup($catalog, $id))) }))')} }`,
        ),

        // The charged total and the per-line prices are Pricing's (ADR-0048 §3/§4, ADR-0050 §5).
        Quote: {
          Type: 'Task',
          Resource: 'arn:aws:states:::lambda:invoke',
          TimeoutSeconds: LAMBDA_TIMEOUT_SECONDS,
          Arguments: {
            FunctionName: quote.functionArn,
            Payload: {
              Items: `{% ${asArray('$map($cartItems, function($i) { { "ProductId": $i.productId, "Quantity": $i.quantity } })')} %}`,
              PointsToUse: '{% $checkoutInput.pointsToUse ? $checkoutInput.pointsToUse : 0 %}',
              ExpectedTotal: '{% $checkoutInput.totalPrice %}',
              PaymentMethod: '{% $checkoutInput.paymentMethod %}',
              Installments: '{% $checkoutInput.installments %}',
            },
          },
          Assign: { quote: '{% $states.result.Payload %}' },
          Retry: lambdaRetry(LAMBDA_TRANSIENT_ERRORS),
          Catch: toInternalError,
          Next: 'QuoteAccepted',
        },
        QuoteAccepted: {
          Type: 'Choice',
          Choices: [
            { Condition: '{% $exists($quote.Rejection) %}', Next: 'RefuseQuote' },
            // Defence in depth: a Pricing that predates per-line prices would price the order lines
            // at zero. Never write an order without one price per cart line.
            { Condition: `{% $count(${asArray('$quote.Lines')}) != $count($cartItems) %}`, Next: 'InternalError' },
          ],
          Default: 'RegisterPayment',
        },
        RefuseQuote: refusal(
          '$quote.Rejection.ErrorType',
          '$quote.Rejection.Message',
          '{ "serverTotal": $quote.Rejection.ServerTotal, "maxRedeemablePoints": $quote.Rejection.MaxRedeemablePoints }',
        ),

        // The card reaches Payment here and nowhere else (ADR-0050 §6/§7).
        RegisterPayment: {
          Type: 'Task',
          Resource: 'arn:aws:states:::lambda:invoke',
          TimeoutSeconds: LAMBDA_TIMEOUT_SECONDS,
          Arguments: {
            FunctionName: registerPayment.functionArn,
            Payload: {
              OrderId: '{% $orderId %}',
              CustomerId: '{% $customerId %}',
              Amount: '{% $quote.ChargedTotal %}',
              PaymentMethod: '{% $checkoutInput.paymentMethod %}',
              Installments: '{% $checkoutInput.installments %}',
              // Absent for cash.
              CardName: optional('$checkoutInput.cardName'),
              CardNumber: optional('$checkoutInput.cardNumber'),
              Expiration: optional('$checkoutInput.expiration'),
              Cvv: optional('$checkoutInput.cvv'),
            },
          },
          Assign: { registration: '{% $states.result.Payload %}' },
          // Output dropped: the task result is only read through $registration.
          Output: '{% {} %}',
          Retry: lambdaRetry(LAMBDA_TRANSIENT_ERRORS),
          Catch: toInternalError,
          Next: 'PaymentRegistered',
        },
        PaymentRegistered: {
          Type: 'Choice',
          Choices: [{ Condition: '{% $exists($registration.Refusal) %}', Next: 'RefusePayment' }],
          Default: 'Checkout',
        },
        RefusePayment: refusal(
          '$registration.Refusal.ErrorType',
          '$registration.Refusal.Message',
          '{ "field": $registration.Refusal.Field }',
        ),

        // The lines are assembled here from their owners: Pricing's unit price (Lines follow the
        // order of the quoted items) and Catalog's name and image. Basket never re-reads the cart.
        Checkout: {
          Type: 'Task',
          Resource: 'arn:aws:states:::lambda:invoke',
          TimeoutSeconds: LAMBDA_TIMEOUT_SECONDS,
          Arguments: {
            FunctionName: checkout.functionArn,
            Payload: {
              CheckoutId: '{% $checkoutId %}',
              CartVersion: '{% $cartVersion %}',
              BasketCheckoutDto: {
                OwnerId: '{% $ownerId %}',
                CustomerId: '{% $customerId %}',
                OrderId: '{% $orderId %}',
                TotalPrice: '{% $quote.ChargedTotal %}',
                PointsToUse: '{% $checkoutInput.pointsToUse ? $checkoutInput.pointsToUse : 0 %}',
                ShippingAddress: {
                  FirstName: '{% $checkoutInput.firstName %}',
                  LastName: '{% $checkoutInput.lastName %}',
                  EmailAddress: '{% $checkoutInput.emailAddress %}',
                  AddressLine: '{% $checkoutInput.addressLine %}',
                  Country: '{% $checkoutInput.country %}',
                  State: '{% $checkoutInput.state %}',
                  ZipCode: '{% $checkoutInput.zipCode %}',
                },
                // No card: it was registered with Payment above (ADR-0050 §7).
                Payment: {
                  PaymentMethod: '{% $checkoutInput.paymentMethod %}',
                  Installments: '{% $checkoutInput.installments %}',
                },
                Items:
                  `{% ${asArray(
                    '$map($cartItems, function($i, $index) { ( $p := $lookup($catalog, $i.productId); ' +
                      '{ "ProductId": $i.productId, "ProductName": $p.name, "ImageId": $p.imageId, ' +
                      '"Quantity": $i.quantity, "Price": ' +
                      asArray('$quote.Lines') +
                      '[$index].UnitPrice } ) })',
                  )} %}`,
              },
            },
          },
          Assign: { checkout: '{% $states.result.Payload %}' },
          Output: '{% {} %}',
          Retry: lambdaRetry(CHECKOUT_RETRYABLE_ERRORS),
          // An error here may have happened after the transaction committed: never compensate it
          // (ADR-0050 §1). If the cart closed, the event promotes the payment; if not, the
          // Registered payment is an inert orphan.
          Catch: toInternalError,
          Next: 'CheckoutOutcome',
        },
        CheckoutOutcome: {
          Type: 'Choice',
          Choices: [
            // Defence in depth: a Basket that predates the saga mints its own OrderId; an id other
            // than the derived one means the close was not the saga's.
            { Condition: '{% $checkout.IsSuccess = true and $checkout.OrderId = $orderId %}', Next: 'CheckedOut' },
            { Condition: "{% $checkout.ErrorType = 'CartChanged' %}", Next: 'VoidPayment' },
          ],
          Default: 'InternalError',
        },
        CheckedOut: {
          Type: 'Succeed',
          Output: '{% { "isSuccess": true, "orderId": $checkout.OrderId } %}',
        },

        // Compensation, only for a typed refusal: the cart provably did not close.
        VoidPayment: {
          Type: 'Task',
          Resource: 'arn:aws:states:::lambda:invoke',
          TimeoutSeconds: LAMBDA_TIMEOUT_SECONDS,
          Arguments: {
            FunctionName: voidPayment.functionArn,
            Payload: { OrderId: '{% $orderId %}' },
          },
          Output: '{% {} %}',
          Retry: lambdaRetry(['States.ALL']),
          Catch: [{ ErrorEquals: ['States.ALL'], Next: 'CompensationFailed' }],
          Next: 'RefuseCartChanged',
        },
        RefuseCartChanged: refusal('"CartChanged"', '"Your cart changed. Review it before checking out."'),

        // No cause: the client gets InternalError and nothing else (ADR-0050 §8).
        InternalError: { Type: 'Fail', Error: 'InternalError', Cause: 'The checkout could not be completed.' },
        CompensationFailed: {
          Type: 'Fail',
          Error: 'CompensationFailed',
          Cause: 'The checkout could not be completed.',
        },
      },
    };

    const logGroup = new logs.LogGroup(this, 'Logs', {
      logGroupName: '/aws/states/checkout-saga',
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    this.stateMachine = new sfn.StateMachine(this, 'StateMachine', {
      stateMachineName: 'checkout-saga',
      stateMachineType: sfn.StateMachineType.EXPRESS,
      definitionBody: sfn.DefinitionBody.fromString(cdk.Stack.of(this).toJsonString(definition)),
      timeout: cdk.Duration.seconds(EXECUTION_TIMEOUT_SECONDS),
      tracingEnabled: true,
      // ERROR without execution data: the input carries the card (ADR-0050 §7).
      logs: { destination: logGroup, level: sfn.LogLevel.ERROR, includeExecutionData: false },
    });

    // A definition given as a string carries no grants, so the role gets exactly what the states use.
    this.stateMachine.addToRolePolicy(
      new iam.PolicyStatement({ actions: ['dynamodb:GetItem'], resources: [cartsTable.tableArn] }),
    );
    this.stateMachine.addToRolePolicy(
      new iam.PolicyStatement({ actions: ['dynamodb:BatchGetItem'], resources: [productsTable.tableArn] }),
    );
    this.stateMachine.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ['lambda:InvokeFunction'],
        resources: [quote, registerPayment, voidPayment, checkout].flatMap(fn => [fn.functionArn, `${fn.functionArn}:*`]),
      }),
    );

    // Refusals succeed, so a failed or timed-out execution is always a real fault: InternalError,
    // CompensationFailed or the execution timeout (ADR-0050 §8).
    const alerts = new cloudwatchActions.SnsAction(importAlertsTopic(this));
    const alarmOn = (alarmId: string, alarmName: string, metric: cloudwatch.IMetric, description: string) =>
      new cloudwatch.Alarm(this, alarmId, {
        alarmName,
        alarmDescription: description,
        metric,
        threshold: 1,
        evaluationPeriods: 1,
        comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
        treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
      }).addAlarmAction(alerts);

    alarmOn(
      'FailedAlarm',
      'checkout-saga-failed',
      this.stateMachine.metricFailed({ period: cdk.Duration.minutes(5), statistic: 'Sum' }),
      'checkout-saga ended in InternalError or CompensationFailed (ADR-0050 §8)',
    );
    alarmOn(
      'TimedOutAlarm',
      'checkout-saga-timed-out',
      this.stateMachine.metricTimedOut({ period: cdk.Duration.minutes(5), statistic: 'Sum' }),
      'checkout-saga hit its execution timeout (ADR-0050 §1)',
    );
  }
}
