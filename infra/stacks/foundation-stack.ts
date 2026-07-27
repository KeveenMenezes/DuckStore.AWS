import * as cdk from 'aws-cdk-lib';
import * as events from 'aws-cdk-lib/aws-events';
import { Construct } from 'constructs';

/** Created by FoundationStack; every other stack imports it by this fixed name. */
export const EVENT_BUS_NAME = 'duckstore-event-bus';

/**
 * Shared EventBridge bus (ADR-0004): every service's integration events flow through
 * this one bus. It used to live inside CatalogStack — a resource read by 7 stacks
 * living inside one of its consumers meant any change that forced Catalog to delete
 * the bus deadlocked on the other six stacks' EventBridge rules. Consumers import it
 * by fixed name (EventBus.fromEventBusName), the same no-CloudFormation-coupling
 * convention as duckstore-alerts (see importAlertsTopic in context-dlq.ts) — this
 * stack just has to deploy first.
 *
 * RemovalPolicy.RETAIN: if this stack is ever deleted, the bus (and its 15+ rules'
 * target) survives instead of the delete blocking on every consumer stack's rules —
 * the same failure mode this stack exists to fix, just moved to a new address.
 */
export class FoundationStack extends cdk.Stack {
  public readonly eventBus: events.EventBus;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    this.eventBus = new events.EventBus(this, 'EventBus', {
      eventBusName: EVENT_BUS_NAME,
    });
    this.eventBus.applyRemovalPolicy(cdk.RemovalPolicy.RETAIN);

    new cdk.CfnOutput(this, 'EventBusName', {
      value: this.eventBus.eventBusName,
      exportName: `${this.stackName}-EventBusName`,
    });
  }
}
