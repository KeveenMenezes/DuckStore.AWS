import * as fs from 'fs';
import * as path from 'path';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as cr from 'aws-cdk-lib/custom-resources';
import { Construct } from 'constructs';

export interface SpaTagCacheSeederProps {
  readonly tagCacheTable: dynamodb.Table;
  /** Path to the SPA's `.open-next` build output. */
  readonly openNextDir: string;
}

/**
 * Seeds the OpenNext tag-cache table with the `{tag, path, revalidatedAt}`
 * rows OpenNext computes at build time for every prerendered ISR page's
 * tagged fetch() calls (`.open-next/dynamodb-provider/dynamodb-cache.json`).
 *
 * Without this, the table stays empty forever: `getByTag()`/`getByPath()`
 * (used by both `revalidateTag()` inside the server function and by
 * SpaTagRevalidator) have no tag -> path mapping to look up, so marking a
 * tag stale is a silent no-op — confirmed in prod: the SpaTagRevalidator
 * Lambda ran successfully on every CatalogUpdatedEvent, logging "Marked 0
 * entries stale" every time, while a direct table scan showed 0 items.
 * OpenNext's own CDK construct runs this as a Lambda-backed custom resource
 * (`dynamodb-provider/index.mjs`); we don't reuse that Lambda directly since
 * its event contract (lowercase `requestType`) doesn't match the CDK
 * Provider framework's standard `RequestType` and isn't documented — plain
 * `AwsCustomResource` `batchWriteItem` calls give the same result with a
 * contract we control.
 *
 * Re-seeds on every new build: each chunk's physicalResourceId is keyed on
 * BUILD_ID, and the row data itself is build-ID-prefixed (must match
 * `OPEN_NEXT_BUILD_ID` set on the server function in spa-lambdas.ts) — rows
 * from a previous build are simply orphaned under a different key prefix,
 * never overwritten, which is harmless.
 */
export class SpaTagCacheSeeder extends Construct {
  constructor(scope: Construct, id: string, props: SpaTagCacheSeederProps) {
    super(scope, id);

    const { tagCacheTable, openNextDir } = props;

    const buildId = fs.readFileSync(path.join(openNextDir, 'assets', 'BUILD_ID'), 'utf8').trim();
    const seedFile = path.join(openNextDir, 'dynamodb-provider', 'dynamodb-cache.json');
    const items: unknown[] = JSON.parse(fs.readFileSync(seedFile, 'utf8'));

    const policy = cr.AwsCustomResourcePolicy.fromStatements([
      new iam.PolicyStatement({
        actions: ['dynamodb:BatchWriteItem'],
        resources: [tagCacheTable.tableArn],
      }),
    ]);

    const CHUNK_SIZE = 25; // DynamoDB BatchWriteItem's hard limit per request.
    for (let i = 0; i < items.length; i += CHUNK_SIZE) {
      const chunk = items.slice(i, i + CHUNK_SIZE);
      new cr.AwsCustomResource(this, `Seed${i / CHUNK_SIZE}`, {
        onUpdate: {
          service: 'DynamoDB',
          action: 'batchWriteItem',
          parameters: {
            RequestItems: {
              [tagCacheTable.tableName]: chunk.map((item) => ({ PutRequest: { Item: item } })),
            },
          },
          physicalResourceId: cr.PhysicalResourceId.of(`tag-cache-seed-${buildId}-${i}`),
        },
        policy,
      });
    }
  }
}
