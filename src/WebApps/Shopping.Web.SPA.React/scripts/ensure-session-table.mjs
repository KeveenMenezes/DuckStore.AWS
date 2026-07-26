// Creates the BFF session table (ADR-0041) in DynamoDB Local before `next dev` starts.
// On AWS the table is provisioned by sst.config.ts; this only covers local dev, where Aspire
// runs DynamoDB Local and injects AWS_ENDPOINT_URL_DYNAMODB (src/AppHost/Program.cs).
//
// Written as .mjs so it runs on plain node — the SPA has no TypeScript script runner, and adding
// one for a 40-line bootstrap script isn't worth the dependency (same reasoning as revalidator/).
//
// Chained explicitly into the `dev` script rather than relying on a `predev` hook: pnpm disables
// implicit pre/post scripts by default (v7+), so a `predev` would silently never run.

import {
  DynamoDBClient,
  CreateTableCommand,
  UpdateTimeToLiveCommand,
  DescribeTableCommand,
  ResourceInUseException,
} from '@aws-sdk/client-dynamodb'

const TABLE_NAME = process.env.SESSIONS_TABLE_NAME ?? 'duckstore-sessions'

const client = new DynamoDBClient({
  endpoint: process.env.AWS_ENDPOINT_URL_DYNAMODB,
})

async function ensureTable() {
  try {
    await client.send(
      new CreateTableCommand({
        TableName: TABLE_NAME,
        BillingMode: 'PAY_PER_REQUEST',
        KeySchema: [{ AttributeName: 'SessionId', KeyType: 'HASH' }],
        AttributeDefinitions: [{ AttributeName: 'SessionId', AttributeType: 'S' }],
      }),
    )
    console.log(`[sessions] created table ${TABLE_NAME}`)
  } catch (error) {
    if (!(error instanceof ResourceInUseException)) throw error
    console.log(`[sessions] table ${TABLE_NAME} already exists`)
  }

  await client.send(new DescribeTableCommand({ TableName: TABLE_NAME }))

  try {
    await client.send(
      new UpdateTimeToLiveCommand({
        TableName: TABLE_NAME,
        TimeToLiveSpecification: { AttributeName: 'ExpiresAt', Enabled: true },
      }),
    )
  } catch {
    // DynamoDB Local rejects re-enabling TTL that is already on — harmless.
  }
}

try {
  await ensureTable()
} catch (error) {
  // Never block the dev server: sign-in requires real Cognito and doesn't work locally anyway
  // (ADR-0017), so a missing session table only matters once someone points the SPA at AWS.
  console.warn(`[sessions] skipped table setup: ${error.message}`)
}
