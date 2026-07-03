import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class BasketDynamoDB extends Construct {
  public readonly shoppingCartsTable: dynamodb.Table;
  public readonly couponsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Stream feeds basket-shopping-carts-event-publisher via CDC (ADR-0005).
    // NEW_IMAGE is enough: the publisher only reads the new state to build the checkout event.
    this.shoppingCartsTable = new dynamodb.Table(this, 'ShoppingCartsTable', {
      tableName: 'shopping-carts',
      // OwnerId is USER#<cognito-sub> (authenticated) or GUEST#<guestId> (visitor).
      partitionKey: { name: 'OwnerId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_IMAGE,
      // Guest carts set ExpiresAt (epoch seconds); user carts omit it so they never expire.
      timeToLiveAttribute: 'ExpiresAt',
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Coupons are read in-process by the Basket aggregate (ADR-0012 merged Discount into Basket).
    // No stream needed — coupons are read-only from the Basket function's perspective.
    this.couponsTable = new dynamodb.Table(this, 'CouponsTable', {
      tableName: 'coupons',
      partitionKey: { name: 'ProductName', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
