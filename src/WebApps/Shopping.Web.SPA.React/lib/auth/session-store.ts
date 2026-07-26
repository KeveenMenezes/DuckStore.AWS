import {
  DynamoDBClient,
  GetItemCommand,
  PutItemCommand,
  DeleteItemCommand,
  UpdateItemCommand,
  ConditionalCheckFailedException,
} from '@aws-sdk/client-dynamodb'
import { marshall, unmarshall } from '@aws-sdk/util-dynamodb'

// Persistence for BFF-owned sessions (ADR-0041). Cognito tokens live here, server-side —
// the browser only ever holds the opaque SessionId. Nothing outside lib/auth/ reads this table.

export const SESSIONS_TABLE = process.env.SESSIONS_TABLE_NAME ?? 'duckstore-sessions'

export type SessionRecord = {
  SessionId: string
  Sub: string
  Email: string
  Name?: string
  AccessToken: string
  IdToken: string
  RefreshToken: string
  /** Epoch seconds. Drives refresh — see ensureFreshTokens() in ./session.ts. */
  AccessExpiresAt: number
  /** Epoch seconds. Absolute session start; the 30-day cap is measured from here, never slid. */
  CreatedAt: number
  /** Epoch seconds. DynamoDB TTL attribute — expiry is reaped by DynamoDB, at no compute cost. */
  ExpiresAt: number
}

// Lazily constructed so importing this module never touches AWS config (e.g. during `next build`).
// Region/credentials come from the ambient environment: Aspire injects dev values locally and the
// Lambda execution role supplies them on AWS.
let client: DynamoDBClient | null = null

function dynamo(): DynamoDBClient {
  client ??= new DynamoDBClient({})
  return client
}

export async function getSessionRecord(sessionId: string): Promise<SessionRecord | null> {
  const res = await dynamo().send(
    new GetItemCommand({
      TableName: SESSIONS_TABLE,
      Key: marshall({ SessionId: sessionId }),
      // The refresh decision must never read a stale replica: a session that was just rotated by a
      // concurrent request would otherwise look expired and trigger a second Cognito exchange.
      ConsistentRead: true,
    }),
  )

  return res.Item ? (unmarshall(res.Item) as SessionRecord) : null
}

export async function putSessionRecord(record: SessionRecord): Promise<void> {
  await dynamo().send(
    new PutItemCommand({
      TableName: SESSIONS_TABLE,
      Item: marshall(record, { removeUndefinedValues: true }),
    }),
  )
}

export async function deleteSessionRecord(sessionId: string): Promise<void> {
  await dynamo().send(
    new DeleteItemCommand({
      TableName: SESSIONS_TABLE,
      Key: marshall({ SessionId: sessionId }),
    }),
  )
}

/**
 * Single-flight token write. Returns false when another request already rotated this session —
 * the caller must then re-read rather than exchanging the refresh token a second time.
 *
 * This is what makes Cognito refresh token rotation safe (ADR-0041 §4): with rotation enabled the
 * previous refresh token is invalidated on use, so two concurrent exchanges would leave one caller
 * holding a dead token. The condition on the *previous* AccessExpiresAt means exactly one caller
 * ever performs the exchange; losers fall inside Cognito's rotation grace period.
 *
 * ExpiresAt is deliberately not touched: the session's absolute lifetime is measured from
 * CreatedAt and must not be extended by activity.
 */
export async function rotateSessionTokens(
  record: SessionRecord,
  previousAccessExpiresAt: number,
): Promise<boolean> {
  try {
    await dynamo().send(
      new UpdateItemCommand({
        TableName: SESSIONS_TABLE,
        Key: marshall({ SessionId: record.SessionId }),
        UpdateExpression:
          'SET AccessToken = :accessToken, IdToken = :idToken, RefreshToken = :refreshToken, AccessExpiresAt = :accessExpiresAt',
        ConditionExpression: 'attribute_exists(SessionId) AND AccessExpiresAt = :previous',
        ExpressionAttributeValues: marshall({
          ':accessToken': record.AccessToken,
          ':idToken': record.IdToken,
          ':refreshToken': record.RefreshToken,
          ':accessExpiresAt': record.AccessExpiresAt,
          ':previous': previousAccessExpiresAt,
        }),
      }),
    )
    return true
  } catch (error) {
    if (error instanceof ConditionalCheckFailedException) return false
    throw error
  }
}
