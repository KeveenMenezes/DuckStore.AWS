import * as cdk from 'aws-cdk-lib';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as subscriptions from 'aws-cdk-lib/aws-sns-subscriptions';
import { Construct } from 'constructs';
import { ALERTS_TOPIC_NAME } from '../constructs/context-dlq';

export interface MonitoringStackProps extends cdk.StackProps {
  /** E-mail subscribed to duckstore-alerts. Optional so synth/deploy of other stacks never blocks on it. */
  readonly alertsEmail?: string;
}

/**
 * Shared alerting: the duckstore-alerts SNS topic every CloudWatch alarm
 * (per-context DLQ alarms, product-images DLQ alarm) publishes to. Other stacks
 * import the topic by its fixed name (importAlertsTopic), so this stack must be
 * deployed before them — same convention as duckstore-event-bus.
 */
export class MonitoringStack extends cdk.Stack {
  public readonly alertsTopic: sns.Topic;

  constructor(scope: Construct, id: string, props: MonitoringStackProps = {}) {
    super(scope, id, props);

    this.alertsTopic = new sns.Topic(this, 'AlertsTopic', {
      topicName: ALERTS_TOPIC_NAME,
    });

    if (props.alertsEmail) {
      // The subscription stays pending until the address confirms it (SNS e-mail opt-in).
      this.alertsTopic.addSubscription(new subscriptions.EmailSubscription(props.alertsEmail));
    } else {
      cdk.Annotations.of(this).addWarningV2(
        'duckstore:monitoring:no-alerts-email',
        'duckstore-alerts has no e-mail subscription — pass -c alertsEmail=<address> or set ALERTS_EMAIL to receive alarm notifications.',
      );
    }
  }
}
