import * as cdk from 'aws-cdk-lib';
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as cloudwatchActions from 'aws-cdk-lib/aws-cloudwatch-actions';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import { Construct } from 'constructs';

/** Created by MonitoringStack; every other stack imports it by this fixed name. */
export const ALERTS_TOPIC_NAME = 'duckstore-alerts';

// Same no-CloudFormation-coupling convention as the duckstore-event-bus import:
// the topic ARN is deterministic (account/region + fixed name), so consumers
// don't take a CloudFormation dependency on MonitoringStack — it just has to
// be deployed first.
export function importAlertsTopic(scope: Construct, id = 'AlertsTopic'): sns.ITopic {
  return sns.Topic.fromTopicArn(
    scope,
    id,
    cdk.Stack.of(scope).formatArn({ service: 'sns', resource: ALERTS_TOPIC_NAME }),
  );
}

export interface ContextDlqProps {
  /** Bounded-context prefix used for the queue/alarm names, e.g. 'catalog' → catalog-dlq. */
  readonly contextName: string;
}

/**
 * One dead-letter queue per bounded context, shared by every async process in it
 * (DynamoDB Streams publishers via onFailure, EventBridge consumers via async-invoke
 * onFailure destination + rule-target DLQ). A non-empty queue means an integration
 * event was dropped after retries — the alarm notifies duckstore-alerts.
 */
export class ContextDlq extends Construct {
  public readonly queue: sqs.Queue;

  constructor(scope: Construct, id: string, props: ContextDlqProps) {
    super(scope, id);

    this.queue = new sqs.Queue(this, 'Queue', {
      queueName: `${props.contextName}-dlq`,
      retentionPeriod: cdk.Duration.days(14),
    });

    const alarm = new cloudwatch.Alarm(this, 'NotEmptyAlarm', {
      alarmName: `${props.contextName}-dlq-not-empty`,
      metric: this.queue.metricApproximateNumberOfMessagesVisible(),
      threshold: 0,
      comparisonOperator: cloudwatch.ComparisonOperator.GREATER_THAN_THRESHOLD,
      evaluationPeriods: 1,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING,
    });
    alarm.addAlarmAction(new cloudwatchActions.SnsAction(importAlertsTopic(this)));
  }
}
