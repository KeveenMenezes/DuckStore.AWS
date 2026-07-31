import * as cdk from 'aws-cdk-lib';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { Construct } from 'constructs';

export class PricingDynamoDB extends Construct {
  public readonly pricesTable: dynamodb.Table;
  public readonly campaignsTable: dynamodb.Table;
  public readonly productDiscountsTable: dynamodb.Table;
  public readonly gatewayCostsTable: dynamodb.Table;
  public readonly processedEventsTable: dynamodb.Table;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    // One item per product, PK=ProductId. Streams drive the PriceChanged CDC publisher so
    // CatalogView keeps the search document's price in sync (ADR-0026/0027).
    this.pricesTable = new dynamodb.Table(this, 'PricesTable', {
      tableName: 'prices',
      partitionKey: { name: 'ProductId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      stream: dynamodb.StreamViewType.NEW_IMAGE,
    });

    // One item per campaign, PK=Id. No stream: the fan-out to product-discounts happens via a
    // plain in-handler TransactWriteItems (internal to Pricing), not a CDC rule (ADR-0026 §6).
    this.campaignsTable = new dynamodb.Table(this, 'CampaignsTable', {
      tableName: 'campaigns',
      partitionKey: { name: 'Id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Denormalized read projection, PK=ProductId — per-product discount lookup is a plain
    // GetItem (ADR-0026); expiry is checked at read time against StartsAt/EndsAt.
    //
    // Streams + TTL exist for one reason (ADR-0044): a campaign write never touches `prices`, so
    // without them nothing wakes the CDC path and CatalogView's denormalized price stays at its
    // pre-campaign value forever. KEYS_ONLY is deliberate — the publisher re-reads the committed
    // discount rather than trusting the stream image. TTL on ExpiresAt (= the campaign's EndsAt)
    // turns a natural expiry into a REMOVE record on that same stream, so an expired campaign
    // rolls the catalog price back with no scheduler (ADR-0026 §7 rejected one).
    this.productDiscountsTable = new dynamodb.Table(this, 'ProductDiscountsTable', {
      tableName: 'product-discounts',
      partitionKey: { name: 'ProductId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      stream: dynamodb.StreamViewType.KEYS_ONLY,
      timeToLiveAttribute: 'ExpiresAt',
    });

    // One item per provider, PK=Provider. No stream — a gateway-cost-only change never fans out
    // to CatalogView on its own; the payment badge only refreshes on the next price change or a
    // manual ProductBackfill re-run (ADR-0028).
    this.gatewayCostsTable = new dynamodb.Table(this, 'GatewayCostsTable', {
      tableName: 'gateway-costs',
      partitionKey: { name: 'Provider', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    // Idempotency inbox for the ProductDeleted consumer.
    this.processedEventsTable = new dynamodb.Table(this, 'ProcessedEventsTable', {
      tableName: 'pricing-processed-events',
      partitionKey: { name: 'PK', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
  }
}
