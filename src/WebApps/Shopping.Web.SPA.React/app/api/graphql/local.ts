import { createYoga, createSchema, createGraphQLError } from 'graphql-yoga'
import { GraphQLScalarType, Kind } from 'graphql'
import {
  DynamoDBClient,
  ScanCommand,
  QueryCommand,
  GetItemCommand,
  BatchGetItemCommand,
  PutItemCommand,
  UpdateItemCommand,
  DeleteItemCommand,
  TransactWriteItemsCommand,
  ConditionalCheckFailedException,
  TransactionCanceledException,
  type AttributeValue,
} from '@aws-sdk/client-dynamodb'
import { LambdaClient, InvokeCommand } from '@aws-sdk/client-lambda'
import { unmarshall } from '@aws-sdk/util-dynamodb'
import { randomUUID } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { prepareBasketRequest, guestCookieHeader } from '@/lib/basket-bff'
import { checkoutOrderId } from '@/lib/checkout-order-id'
import {
  normalizeWarehouseCode,
  normalizeWarehouseFields,
  type WarehouseFields,
  type WarehouseFieldsInput,
} from '@/lib/warehouse-validation'
import type { Owner } from '@/lib/identity'

type LocalContext = { owner: Owner }

// Ordering CustomerId is a Guid; locally we derive it from the resolved owner (prefix stripped).
function customerIdFromOwner(ownerId: string): string {
  return ownerId.replace(/^(USER#|GUEST#)/, '')
}

interface ProductImageInput {
  imageId: string
  isMain: boolean
  order: number
}

// Image metadata (ADR-0034) — unmarshalled Images list from a product item → GraphQL shape.
function mapImages(images: unknown): Array<{ imageId: string; isMain: boolean; order: number }> {
  if (!Array.isArray(images)) return []
  return images.map(img => ({
    imageId: img.ImageId as string,
    isMain: Boolean(img.IsMain),
    order: Number(img.Order ?? 0),
  }))
}

// GraphQL ProductImageInput → the Images list-of-maps attribute, in DynamoDB wire format —
// the same shape the AppSync resolvers and the create saga write (ADR-0034).
function marshalImages(images: ProductImageInput[]) {
  return {
    L: images.map(i => ({
      M: {
        ImageId: { S: i.imageId },
        IsMain: { BOOL: i.isMain },
        Order: { N: String(i.order) },
      },
    })),
  }
}

// Strip AppSync-only auth directives — graphql-yoga's schema builder doesn't know them.
// AppSync provides the AWSJSON scalar built in; graphql-yoga doesn't, so it's declared and
// resolved below as a passthrough (arbitrary JSON value in, same value out).
const typeDefs =
  'scalar AWSJSON\n' +
  // Built into AppSync too; ISO-8601 strings pass straight through, so no scalar class is needed.
  'scalar AWSDateTime\n' +
  // The schema lives at the monorepo root (ADR-0033) — shared contract, not SPA code.
  // cwd is the SPA directory when Next.js runs, so reach up to the repo root.
  readFileSync(join(process.cwd(), '../../../graphql/schema.graphql'), 'utf-8')
    .replace(/\s*@aws_api_key\b/g, '')
    .replace(/\s*@aws_cognito_user_pools\b/g, '')

const awsJsonScalar = new GraphQLScalarType({
  name: 'AWSJSON',
  description: 'Arbitrary JSON value, passed through as-is (local dev stand-in for AppSync AWSJSON).',
  serialize: (value) => value,
  parseValue: (value) => value,
  parseLiteral: (ast) => {
    if (ast.kind === Kind.STRING) return JSON.parse(ast.value)
    throw createGraphQLError('AWSJSON literals must be strings containing JSON')
  },
})

// AWS SDK v3 respects AWS_ENDPOINT_URL_DYNAMODB automatically, but we set it explicitly
// because Aspire injects it via WithReference(dynamoDb).
const dynamoDb = new DynamoDBClient({
  region: process.env.AWS_REGION ?? 'us-east-1',
  ...(process.env.AWS_ENDPOINT_URL_DYNAMODB
    ? { endpoint: process.env.AWS_ENDPOINT_URL_DYNAMODB }
    : {}),
  credentials: {
    accessKeyId: process.env.AWS_ACCESS_KEY_ID ?? 'dummy',
    secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY ?? 'dummy',
  },
})

// AWS_ENDPOINT_URL_LAMBDA is injected by Aspire via lambdaEmulator.GetEndpoint("http").
// The Lambda emulator implements the real Invoke API at /2015-03-31/functions/{name}/invocations.
const lambdaClient = new LambdaClient({
  endpoint: process.env.AWS_ENDPOINT_URL_LAMBDA,
  region: process.env.AWS_REGION ?? 'us-east-1',
  credentials: {
    accessKeyId: process.env.AWS_ACCESS_KEY_ID ?? 'dummy',
    secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY ?? 'dummy',
  },
})

async function invokeLambda<T>(functionName: string, payload: unknown): Promise<T> {
  const result = await lambdaClient.send(
    new InvokeCommand({
      FunctionName: functionName,
      Payload: JSON.stringify(payload),
    }),
  )
  const body = new TextDecoder().decode(result.Payload)
  if (result.FunctionError) throw new Error(`Lambda ${functionName} error: ${body}`)
  return JSON.parse(body) as T
}

// Ordering.Function stores PaymentMethod as the C# enum's string name (Payment.cs .ToString()),
// not its numeric value — the GraphQL schema declares paymentMethod as Int!, so it must be
// converted here. Keep in sync with Ordering.Function.Modules.Orders.Domain.Enums.PaymentMethod.
const PAYMENT_METHOD_TO_INT: Record<string, number> = { Debit: 1, Credit: 2, Cash: 3 }

// Challenges.Function stores Difficulty as the C# enum's PascalCase name (Question.cs
// .ToString()); the GraphQL enum is uppercase (ADR-0045 §2). Keep in sync with
// Challenges.Function.Modules.Questions.Domain.Enums.Difficulty.
const DIFFICULTY_TO_STORED: Record<string, string> = { EASY: 'Easy', MEDIUM: 'Medium', HARD: 'Hard' }

function mapChallenge(item: Record<string, unknown>) {
  return {
    id: item.QuestionId as string,
    title: item.Title as string,
    description: item.Description as string,
    code: item.Code as string,
    options: Array.isArray(item.Options) ? (item.Options as string[]) : [],
    difficulty: ((item.Difficulty as string) ?? '').toUpperCase(),
    language: item.Language as string,
    points: Number(item.Points ?? 0),
    hintCount: Number(item.HintCount ?? 0),
  }
}

function mapOrder(item: Record<string, unknown>) {
  const addr = (item.ShippingAddress ?? {}) as Record<string, string>
  const pay = (item.Payment ?? {}) as Record<string, unknown>
  const rawItems = Array.isArray(item.OrderItems)
    ? (item.OrderItems as Array<Record<string, unknown>>)
    : []
  return {
    id: item.Id as string,
    customerId: item.CustomerId as string,
    orderName: item.OrderName as string,
    status: String(item.Status),
    createdAt: (item.CreatedAt as string) ?? null,
    shippingAddress: {
      firstName: addr.FirstName ?? '',
      lastName: addr.LastName ?? '',
      emailAddress: addr.EmailAddress ?? '',
      addressLine: addr.AddressLine ?? '',
      country: addr.Country ?? '',
      state: addr.State ?? '',
      zipCode: addr.ZipCode ?? '',
    },
    payment: {
      paymentMethod: PAYMENT_METHOD_TO_INT[pay.PaymentMethod as string] ?? 0,
      installments: (pay.Installments as number) ?? 1,
    },
    orderItems: rawItems.map(oi => ({
      productId: String(oi.ProductId ?? ''),
      productName: String(oi.ProductName ?? ''),
      imageId: (oi.ImageId as string) ?? null,
      quantity: Number(oi.Quantity ?? 0),
      price: Number(oi.Price ?? 0),
    })),
  }
}

// reviews row → GraphQL Review, mirroring Query.myReview.js/Query.reviewsByProduct.js. An
// Eligible row has no Rating/Comment/UserName yet, and a row without Status is legacy = Published
// (ADR-0049 §1).
function mapReview(item: Record<string, unknown>) {
  return {
    id: item.Id as string,
    productId: item.ProductId as string,
    userName: (item.UserName as string | undefined) ?? '',
    rating: Number(item.Rating ?? 0),
    comment: (item.Comment as string | undefined) ?? '',
    createdAt: item.CreatedAt as string,
    updatedAt: (item.UpdatedAt as string | undefined) ?? (item.CreatedAt as string),
    status: (item.Status as string | undefined) ?? 'Published',
  }
}

// AppSync's util.unauthorized() message, so the SPA sees the same error locally.
function unauthorized(field: string, type: 'Query' | 'Mutation' = 'Mutation') {
  return createGraphQLError(`Not Authorized to access ${field} on type ${type}`, {
    extensions: { errorType: 'Unauthorized' },
  })
}

const REVIEW_TTL_SECONDS = 5 * 24 * 60 * 60

// Same ceiling as Mutation.updateChallengePoints.js (MAX_POINTS) and the Blazor ChallengePointsInput.
const MAX_CHALLENGE_POINTS = 1000

// Inventory warehouses (ADR-0052) — mirrors graphql/resolvers/inventory. The table holds warehouses
// and their CODE#<code> guards, so every read filters on Type.
const WAREHOUSES_TABLE = 'inventory-warehouses'

function mapWarehouse(item: Record<string, unknown>) {
  return {
    id: item.WarehouseId as string,
    code: item.Code as string,
    name: item.Name,
    address: {
      street: item.Street,
      number: item.Number,
      complement: item.Complement ?? null,
      district: item.District,
      city: item.City,
      state: item.State,
      zipCode: item.ZipCode,
    },
    priority: Number(item.Priority),
    active: Boolean(item.Active),
    createdAt: item.CreatedAt,
    updatedAt: item.UpdatedAt,
  }
}

// Same errorType and errorInfo as the resolvers' util.error(msg, 'BadRequest', null, { field }).
function warehouseValidated<T>(result: { value?: T; error?: { field: string; message: string } }): T {
  if (result.error) {
    throw createGraphQLError(result.error.message, {
      extensions: { errorType: 'BadRequest', errorInfo: { field: result.error.field } },
    })
  }
  return result.value as T
}

function warehouseAttributes(fields: WarehouseFields): Record<string, AttributeValue> {
  return {
    Name: { S: fields.name },
    Street: { S: fields.street },
    Number: { S: fields.number },
    District: { S: fields.district },
    City: { S: fields.city },
    State: { S: fields.state },
    ZipCode: { S: fields.zipCode },
    Priority: { N: String(fields.priority) },
    ...(fields.complement ? { Complement: { S: fields.complement } } : {}),
  }
}

function warehouseNotFound() {
  return createGraphQLError('Warehouse not found', { extensions: { errorType: 'NotFound' } })
}

// Locally there is no Cognito identity to record, so UpdatedBy is a fixed marker.
const LOCAL_WAREHOUSE_AUTHOR = 'local-dev'

// The saved cart's lines as Pricing quotes them — the checkout pipeline's loadCart step (Data is
// the .NET-serialized PascalCase JSON).
// A typed checkout refusal, shaped like the AppSync resolver's (ADR-0050 §8): the SPA branches on
// errorType and reads errorInfo, whichever backend raised it.
function refusal(errorType: string, message: string, errorInfo: Record<string, unknown> = {}) {
  return createGraphQLError(message, { extensions: { errorType, errorInfo } })
}

function internalError() {
  return createGraphQLError('The checkout could not be completed.', { extensions: { errorType: 'InternalError' } })
}

async function pointsBalance(ownerId: string): Promise<number> {
  const result = await dynamoDb.send(
    new GetItemCommand({
      TableName: 'challenge-progress',
      Key: { OwnerId: { S: ownerId }, SK: { S: 'PROFILE' } },
      ConsistentRead: true,
    }),
  )
  return result.Item ? Number(unmarshall(result.Item).Score ?? 0) : 0
}

// Local stand-in for the whole redemption flow (ADR-0048 §5), which on AWS runs over EventBridge:
// challenges-basket-checkout-consumer reserves (debit + REDEMPTION#<orderId> Reserved), and the
// payment authorization turns it Used. Locally the payment always goes through, so the row is
// written Used directly, in the same transaction as the debit and under the same balance guard.
async function redeemPointsForOrder(ownerId: string, orderId: string, points: number) {
  const now = new Date().toISOString()
  await dynamoDb.send(
    new TransactWriteItemsCommand({
      TransactItems: [
        {
          Update: {
            TableName: 'challenge-progress',
            Key: { OwnerId: { S: ownerId }, SK: { S: 'PROFILE' } },
            ConditionExpression: 'Score >= :points',
            UpdateExpression: 'ADD Score :debit',
            ExpressionAttributeValues: {
              ':points': { N: String(points) },
              ':debit': { N: String(-points) },
            },
          },
        },
        {
          Put: {
            TableName: 'points-transactions',
            Item: {
              OwnerId: { S: ownerId },
              TransactionId: { S: `REDEMPTION#${orderId}` },
              Type: { S: 'Redemption' },
              Status: { S: 'Used' },
              Points: { N: String(-points) },
              SourceId: { S: orderId },
              OrderId: { S: orderId },
              CreatedAt: { S: now },
              UpdatedAt: { S: now },
            },
            ConditionExpression: 'attribute_not_exists(TransactionId)',
          },
        },
      ],
    }),
  )
}

// Local stand-in for review-order-completed-consumer: EventBridge doesn't exist locally, so the
// Ordering → OrderCompletedEvent → Review path never runs, and the checkout creates the Eligible
// rows synchronously instead (ADR-0049 Mitigation). Same writes as the consumer: one conditional
// PutItem per product, no Rating/Comment/GSI1 (sparse GSI1 keeps it out of reviewsByProduct), and
// an existing row — Eligible, Published or Deleted — is left untouched.
async function createEligibleReviews(userId: string, productIds: string[]) {
  const createdAt = new Date().toISOString()
  for (const productId of productIds) {
    try {
      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'reviews',
          Item: {
            Id: { S: `${productId}#${userId}` },
            ProductId: { S: productId },
            UserId: { S: userId },
            Status: { S: 'Eligible' },
            CreatedAt: { S: createdAt },
          },
          ConditionExpression: 'attribute_not_exists(Id)',
        }),
      )
    } catch (err) {
      if (!(err instanceof ConditionalCheckFailedException)) throw err
    }
  }
}

type BasketQuote = {
  TotalOriginalPrice: number
  Price: number
  CashPrice: number
  MaxInstallmentsWithoutInterest: number
  InstallmentPlan: { Count: number; Value: number; TotalValue: number; HasInterest: boolean }[]
  MaxRedeemablePoints: number
  PointsDiscount: number
  ChargedTotal: number | null
  Lines?: { ProductId: string; Quantity: number; UnitPrice: number }[]
  Rejection: {
    ErrorType: string
    Message: string
    ServerTotal: number | null
    MaxRedeemablePoints: number | null
  } | null
}

// Invokes Pricing's GetBasketInstallmentPlan and raises a refused quote (PointsAboveCap,
// PriceChanged) as the same typed error AppSync raises from the resolvers' Rejection mapping.
async function quoteBasket(payload: Record<string, unknown>): Promise<BasketQuote> {
  const body = await invokeLambda<BasketQuote>('pricing-get-basket-installment-plan', payload)
  if (body.Rejection) {
    throw createGraphQLError(body.Rejection.Message, {
      extensions: {
        errorType: body.Rejection.ErrorType,
        errorInfo: {
          serverTotal: body.Rejection.ServerTotal,
          maxRedeemablePoints: body.Rejection.MaxRedeemablePoints,
        },
      },
    })
  }
  return body
}

function mapBasketQuote(body: BasketQuote) {
  return {
    totalOriginalPrice: body.TotalOriginalPrice,
    price: body.Price,
    cashPrice: body.CashPrice,
    maxInstallmentsWithoutInterest: body.MaxInstallmentsWithoutInterest,
    installments: (body.InstallmentPlan ?? []).map(e => ({
      count: e.Count,
      value: e.Value,
      totalValue: e.TotalValue,
      hasInterest: e.HasInterest,
    })),
    maxRedeemablePoints: body.MaxRedeemablePoints ?? 0,
    pointsDiscount: body.PointsDiscount ?? 0,
  }
}

// Same value as PointsPolicy.ReviewCredit in Challenges.Function.
const REVIEW_CREDIT_POINTS = 15

// Local stand-in for challenges-review-created-consumer (same reason as createEligibleReviews):
// one transaction Puts the REVIEW#<productId> ledger row and ADDs to PROFILE Score. A row already
// there cancels the transaction, which is the once-per-product lock — so it is not an error.
// Challenges only scores Cognito users (ADR-0045 §7), so a visitor's owner is skipped.
async function creditReviewPoints(ownerId: string, productId: string) {
  if (!ownerId.startsWith('USER#')) return
  const now = new Date().toISOString()
  try {
    await dynamoDb.send(
      new TransactWriteItemsCommand({
        TransactItems: [
          {
            Put: {
              TableName: 'points-transactions',
              Item: {
                OwnerId: { S: ownerId },
                TransactionId: { S: `REVIEW#${productId}` },
                Type: { S: 'ReviewCredit' },
                Status: { S: 'Completed' },
                Points: { N: String(REVIEW_CREDIT_POINTS) },
                SourceId: { S: productId },
                CreatedAt: { S: now },
                UpdatedAt: { S: now },
              },
              ConditionExpression: 'attribute_not_exists(TransactionId)',
            },
          },
          {
            Update: {
              TableName: 'challenge-progress',
              Key: { OwnerId: { S: ownerId }, SK: { S: 'PROFILE' } },
              UpdateExpression: 'ADD Score :points',
              ExpressionAttributeValues: { ':points': { N: String(REVIEW_CREDIT_POINTS) } },
            },
          },
        ],
      }),
    )
  } catch (err) {
    const alreadyCredited =
      err instanceof TransactionCanceledException &&
      err.CancellationReasons?.[0]?.Code === 'ConditionalCheckFailed'
    if (!alreadyCredited) throw err
  }
}

// Lambda JSON uses PascalCase (DefaultLambdaJsonSerializer, no camelCase policy).
const resolvers = {
  AWSJSON: awsJsonScalar,
  Query: {
    // Direct DynamoDB on CatalogView's "catalogview-products" table (ADR-0030). Mirrors
    // graphql/resolvers/products/queries/Query.products.js — Scan + contains() filter, no
    // relevance ranking; sortBy is best-effort (only sorts this page).
    async products(
      _: unknown,
      args: {
        query?: string
        sortBy?: string
        minRating?: number
        maxRating?: number
        pageSize?: number
        nextToken?: string
      },
    ) {
      const expressionNames: Record<string, string> = {}
      const expressionValues: Record<string, AttributeValue> = {}
      const clauses: string[] = []

      if (args.query) {
        expressionNames['#Name'] = 'Name'
        expressionNames['#Description'] = 'Description'
        expressionValues[':q'] = { S: args.query }
        clauses.push('(contains(#Name, :q) OR contains(#Description, :q))')
      }

      if (args.minRating != null || args.maxRating != null) {
        expressionNames['#AverageRating'] = 'AverageRating'
        expressionValues[':min'] = { N: String(args.minRating ?? 0) }
        expressionValues[':max'] = { N: String(args.maxRating ?? 5) }
        clauses.push('#AverageRating BETWEEN :min AND :max')
      }

      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'catalogview-products',
          Limit: args.pageSize ?? 20,
          ...(clauses.length > 0
            ? {
                FilterExpression: clauses.join(' AND '),
                ExpressionAttributeNames: expressionNames,
                ExpressionAttributeValues: expressionValues,
              }
            : {}),
          ...(args.nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(args.nextToken, 'base64').toString()) }
            : {}),
        }),
      )

      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return {
          id: item.Id as string,
          name: item.Name as string,
          description: item.Description as string,
          images: mapImages(item.Images),
          stock: Number(item.Stock ?? 0),
          categoryIds: Array.isArray(item.CategoryIds) ? (item.CategoryIds as string[]) : [],
          averageRating: item.AverageRating ? Number(item.AverageRating) : 0,
          ratingCount: item.RatingCount ? Number(item.RatingCount) : 0,
          ratingDistribution: (item.RatingDistribution as Record<string, number>) ?? {},
          originalPrice: item.OriginalPrice ? Number(item.OriginalPrice) : 0,
          price: item.Price ? Number(item.Price) : 0,
          cashPrice: item.CashPrice ? Number(item.CashPrice) : 0,
          maxInstallmentsWithoutInterest: item.MaxInstallmentsWithoutInterest
            ? Number(item.MaxInstallmentsWithoutInterest)
            : 0,
          maxInstallmentValue: item.MaxInstallmentValue ? Number(item.MaxInstallmentValue) : 0,
        }
      })

      // Best-effort sort — only orders the current page, not the full result set (ADR-0030).
      if (args.sortBy === 'AVERAGE_RATING') {
        items.sort((a, b) => b.averageRating - a.averageRating)
      }

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    async product(_: unknown, { id }: { id: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'catalogview-products', Key: { Id: { S: id } } }),
      )
      if (!result.Item) return null
      const item = unmarshall(result.Item)

      return {
        id: item.Id as string,
        name: item.Name as string,
        description: item.Description as string,
        images: mapImages(item.Images),
        stock: Number(item.Stock ?? 0),
        categoryIds: Array.isArray(item.CategoryIds) ? (item.CategoryIds as string[]) : [],
        averageRating: item.AverageRating ? Number(item.AverageRating) : 0,
        ratingCount: item.RatingCount ? Number(item.RatingCount) : 0,
        ratingDistribution: (item.RatingDistribution as Record<string, number>) ?? {},
        originalPrice: item.OriginalPrice ? Number(item.OriginalPrice) : 0,
        price: item.Price ? Number(item.Price) : 0,
        cashPrice: item.CashPrice ? Number(item.CashPrice) : 0,
        maxInstallmentsWithoutInterest: item.MaxInstallmentsWithoutInterest
          ? Number(item.MaxInstallmentsWithoutInterest)
          : 0,
        maxInstallmentValue: item.MaxInstallmentValue ? Number(item.MaxInstallmentValue) : 0,
      }
    },

    async categories(
      _: unknown,
      { pageSize = 20, nextToken }: { pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'categories',
          Limit: pageSize,
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )

      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return {
          id: item.Id as string,
          name: item.Name as string,
          parentId: (item.ParentId as string) ?? null,
          path: Array.isArray(item.Path) ? (item.Path as string[]) : [],
        }
      })

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    async basket(_: unknown, { ownerId }: { ownerId: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'shopping-carts', Key: { OwnerId: { S: ownerId } } }),
      )
      if (!result.Item) return null
      const item = unmarshall(result.Item)
      // Basket is stored as a JSON blob (PascalCase from .NET serializer) in the Data attribute.
      const cart = JSON.parse(item.Data as string) as {
        OwnerId: string
        Items: Array<{
          Quantity: number
          Color: string
          Price: number
          ProductId: string
          ProductName: string
          ImageId?: string | null
        }>
        TotalPrice: number
      }
      return {
        ownerId: cart.OwnerId,
        items: (cart.Items ?? []).map(i => ({
          quantity: i.Quantity,
          color: i.Color || null,
          price: Number(i.Price),
          productId: String(i.ProductId),
          productName: i.ProductName,
          imageId: i.ImageId ?? null,
        })),
        totalPrice: Number(cart.TotalPrice),
      }
    },

    async orders(
      _: unknown,
      { pageSize = 10, nextToken }: { pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'ordering',
          FilterExpression: '#Type = :type',
          ExpressionAttributeNames: { '#Type': 'Type' },
          ExpressionAttributeValues: { ':type': { S: 'Order' } },
          Limit: pageSize,
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )
      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return mapOrder(item)
      })
      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null
      return { items, nextToken: nextTokenOut }
    },

    async ordersByName(
      _: unknown,
      { name, pageSize = 10, nextToken }: { name: string; pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'ordering',
          FilterExpression: 'contains(OrderName, :name) AND #Type = :type',
          ExpressionAttributeNames: { '#Type': 'Type' },
          ExpressionAttributeValues: { ':name': { S: name }, ':type': { S: 'Order' } },
          Limit: pageSize,
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )
      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return mapOrder(item)
      })
      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null
      return { items, nextToken: nextTokenOut }
    },

    // Direct DynamoDB Scan (ADR-0009) — mirrors the AppSync campaigns resolver. Local dev has no
    // Cognito identity/group check (see orders/ordersByName above for the same limitation).
    async campaigns(
      _: unknown,
      { pageSize = 10, nextToken }: { pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'campaigns',
          Limit: pageSize,
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )
      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return {
          id: item.Id as string,
          name: item.Name as string,
          discountType: item.DiscountType as string,
          value: Number(item.Value),
          startsAt: item.StartsAt as string,
          endsAt: item.EndsAt as string,
          productIds: Array.isArray(item.ProductIds) ? (item.ProductIds as string[]) : [],
          status: item.Status as string,
        }
      })
      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null
      return { items, nextToken: nextTokenOut }
    },

    // Direct DynamoDB GSI1 query (ADR-0009) — mirrors the AppSync ordersByCustomer resolver.
    // Local dev has no Cognito identity, so it scopes by the client-supplied customerId.
    async ordersByCustomer(_: unknown, { customerId }: { customerId: string }) {
      const result = await dynamoDb.send(
        new QueryCommand({
          TableName: 'ordering',
          IndexName: 'GSI1',
          KeyConditionExpression: 'GSI1PK = :pk',
          ExpressionAttributeValues: { ':pk': { S: `CUSTOMER#${customerId}` } },
          ScanIndexForward: false,
        }),
      )
      const items = (result.Items ?? []).map(raw => mapOrder(unmarshall(raw)))
      return { items, nextToken: null }
    },

    // Direct DynamoDB GetItem on Pricing's "prices" table (ADR-0026: a key lookup needs no Lambda).
    async nominalPriceFor(_: unknown, { productId }: { productId: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'prices', Key: { ProductId: { S: productId } } }),
      )
      if (!result.Item) return null
      const item = unmarshall(result.Item)
      return {
        productId: item.ProductId,
        nominalPrice: Number(item.NominalPrice),
        cost: Number(item.Cost ?? 0),
        updatedAt: item.UpdatedAt,
      }
    },

    // Non-trivial calculation — Lambda resolver (ADR-0009).
    async installmentPlanFor(_: unknown, { productId }: { productId: string }) {
      const body = await invokeLambda<{
        ProductId: string
        OriginalPrice: number
        Price: number
        CashPrice: number
        MaxInstallmentsWithoutInterest: number
        InstallmentPlan: { Count: number; Value: number; TotalValue: number; HasInterest: boolean }[]
      }>('pricing-get-installment-plan', { ProductId: productId })
      return {
        productId: body.ProductId,
        originalPrice: body.OriginalPrice,
        price: body.Price,
        cashPrice: body.CashPrice,
        maxInstallmentsWithoutInterest: body.MaxInstallmentsWithoutInterest,
        installments: (body.InstallmentPlan ?? []).map(e => ({
          count: e.Count,
          value: e.Value,
          totalValue: e.TotalValue,
          hasInterest: e.HasInterest,
        })),
      }
    },

    // Mirrors Query.basketInstallmentPlan.js: sums cost/originalPrice across every cart item, then
    // runs the whole cart through the same cost-floor calculation as a single checkout transaction
    // (ADR-0009). A positive pointsToUse is Cognito-only (ADR-0048 §3) — the BFF-resolved owner
    // stands in for the identity, so a visitor is rejected the same way.
    async basketInstallmentPlan(
      _: unknown,
      {
        items,
        pointsToUse,
      }: {
        items: Array<{ productId: string; quantity: number }>
        pointsToUse?: number | null
      },
      context: LocalContext,
    ) {
      const points = pointsToUse ?? 0
      const isGuest = context.owner.ownerId.startsWith('GUEST#')
      if (points > 0 && isGuest) throw unauthorized('basketInstallmentPlan', 'Query')

      const body = await quoteBasket({
        Items: items.map(i => ({ ProductId: i.productId, Quantity: i.quantity })),
        ...(points > 0 ? { PointsToUse: points } : {}),
      })
      return mapBasketQuote(body)
    },

    async reviewsByProduct(
      _: unknown,
      { productId, pageSize = 20, nextToken }: { productId: string; pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new QueryCommand({
          TableName: 'reviews',
          IndexName: 'GSI1',
          KeyConditionExpression: 'GSI1PK = :pk',
          ExpressionAttributeValues: { ':pk': { S: productId } },
          ScanIndexForward: false,
          Limit: pageSize,
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )

      const items = (result.Items ?? []).map(raw => mapReview(unmarshall(raw)))

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    // Mirrors Query.myReview.js: GetItem on productId#<owner>; null when the customer never bought
    // the product (no row). The owner stands in for ctx.identity.sub locally.
    async myReview(_: unknown, { productId }: { productId: string }, context: LocalContext) {
      const userId = customerIdFromOwner(context.owner.ownerId)
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'reviews', Key: { Id: { S: `${productId}#${userId}` } } }),
      )
      return result.Item ? mapReview(unmarshall(result.Item)) : null
    },

    async myProfile(_: unknown, __: unknown, context: LocalContext) {
      // Profile is Cognito-only in prod; locally there is no Cognito, so we derive the id from
      // the BFF-resolved owner and seed with placeholder claims (same limitation as local checkout).
      // Mirrors the AppSync direct UpdateItem resolver (ADR-0009): if_not_exists lazily
      // provisions Email/Name once and never overwrites them on later calls.
      const userId = customerIdFromOwner(context.owner.ownerId)
      const result = await dynamoDb.send(
        new UpdateItemCommand({
          TableName: 'user-profiles',
          Key: { UserId: { S: userId } },
          UpdateExpression: 'SET Email = if_not_exists(Email, :email), #name = if_not_exists(#name, :name)',
          ExpressionAttributeNames: { '#name': 'Name' },
          ExpressionAttributeValues: {
            ':email': { S: `${userId}@local.dev` },
            ':name': { S: userId },
          },
          ReturnValues: 'ALL_NEW',
        }),
      )
      const item = unmarshall(result.Attributes ?? {})
      return {
        userId: item.UserId as string,
        email: item.Email as string,
        name: item.Name as string,
        phone: (item.Phone as string) ?? null,
        addressLine: (item.AddressLine as string) ?? null,
        city: (item.City as string) ?? null,
        state: (item.State as string) ?? null,
        zipCode: (item.ZipCode as string) ?? null,
        country: (item.Country as string) ?? null,
      }
    },

    // Mirrors the AppSync direct resolver (ADR-0045 §2/§8): GSI1 is sparse (only the PUBLIC item
    // carries GSI1PK/GSI1SK), so the ANSWER item can never leak through either path. Both branches
    // go through that index — Scanning it instead of the base table keeps `limit` honest, since
    // DynamoDB caps items read before filtering and each question is two base-table items.
    async challenges(
      _: unknown,
      { language, difficulty, pageSize, nextToken }:
        { language?: string; difficulty?: string; pageSize?: number; nextToken?: string },
    ) {
      const limit = pageSize ?? 20
      const storedDifficulty = difficulty ? DIFFICULTY_TO_STORED[difficulty] : undefined

      const result = language
        ? await dynamoDb.send(
            new QueryCommand({
              TableName: 'challenges',
              IndexName: 'GSI1',
              KeyConditionExpression: storedDifficulty
                ? 'GSI1PK = :pk AND begins_with(GSI1SK, :difficultyPrefix)'
                : 'GSI1PK = :pk',
              ExpressionAttributeValues: storedDifficulty
                ? { ':pk': { S: language }, ':difficultyPrefix': { S: `${storedDifficulty}#` } }
                : { ':pk': { S: language } },
              Limit: limit,
              ...(nextToken
                ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
                : {}),
            }),
          )
        : await dynamoDb.send(
            new ScanCommand({
              TableName: 'challenges',
              IndexName: 'GSI1',
              ...(storedDifficulty
                ? {
                    FilterExpression: 'Difficulty = :difficulty',
                    ExpressionAttributeValues: { ':difficulty': { S: storedDifficulty } },
                  }
                : {}),
              Limit: limit,
              ...(nextToken
                ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
                : {}),
            }),
          )

      const items = (result.Items ?? []).map(raw => mapChallenge(unmarshall(raw)))
      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    async challenge(_: unknown, { id }: { id: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({
          TableName: 'challenges',
          Key: { QuestionId: { S: id }, SK: { S: 'PUBLIC' } },
        }),
      )
      if (!result.Item) return null
      return mapChallenge(unmarshall(result.Item))
    },

    // Cognito-only in prod; guests play but don't score (ADR-0045 §7). Locally there is no
    // Cognito, so the same BFF-resolved owner used for basket/reviews stands in for the identity.
    async myChallengeProgress(_: unknown, __: unknown, context: LocalContext) {
      const result = await dynamoDb.send(
        new QueryCommand({
          TableName: 'challenge-progress',
          KeyConditionExpression: 'OwnerId = :ownerId',
          ExpressionAttributeValues: { ':ownerId': { S: context.owner.ownerId } },
        }),
      )

      const items = (result.Items ?? []).map(raw => unmarshall(raw))
      const profile = items.find(item => item.SK === 'PROFILE')
      const attemptItems = items.filter(
        item => typeof item.SK === 'string' && item.SK.startsWith('ATTEMPT#') && item.IsCorrect !== undefined,
      )

      const byLanguage: Record<string, number> = {}
      if (profile) {
        for (const key of Object.keys(profile)) {
          if (key.startsWith('Lang#')) byLanguage[key.slice(5)] = Number(profile[key])
        }
      }

      return {
        score: Number(profile?.Score ?? 0),
        completed: Number(profile?.Completed ?? 0),
        correctCount: Number(profile?.CorrectCount ?? 0),
        wrongCount: Number(profile?.WrongCount ?? 0),
        hintsUsed: Number(profile?.HintsUsed ?? 0),
        currentStreak: Number(profile?.CurrentStreak ?? 0),
        lastAnsweredAt: (profile?.LastAnsweredAt as string) ?? null,
        byLanguage,
        attempts: attemptItems.map(item => ({
          questionId: (item.SK as string).slice(8),
          isCorrect: Boolean(item.IsCorrect),
          selectedOption: Number(item.SelectedOption ?? 0),
          hintsRevealed: Number(item.HintsRevealed ?? 0),
          pointsEarned: Number(item.PointsEarned ?? 0),
          answeredAt: (item.AnsweredAt as string) ?? null,
        })),
      }
    },

    // Mirrors Query.myPointsHistory.js (ADR-0048 §1): LSI1 Query, newest first, paginated. Cognito
    // only in prod; locally the BFF-resolved owner stands in for the identity, so a visitor is
    // rejected the way AppSync rejects a call without a user-pool token. Keep the page-size clamp
    // and the createdAt trimming in sync with the resolver.
    async myPointsHistory(
      _: unknown,
      { pageSize, nextToken }: { pageSize?: number; nextToken?: string },
      context: LocalContext,
    ) {
      if (context.owner.ownerId.startsWith('GUEST#')) throw unauthorized('myPointsHistory', 'Query')

      const result = await dynamoDb.send(
        new QueryCommand({
          TableName: 'points-transactions',
          IndexName: 'LSI1',
          KeyConditionExpression: 'OwnerId = :ownerId',
          ExpressionAttributeValues: { ':ownerId': { S: context.owner.ownerId } },
          ScanIndexForward: false,
          Limit: Math.max(1, Math.min(pageSize ?? 20, 100)),
          ...(nextToken
            ? { ExclusiveStartKey: JSON.parse(Buffer.from(nextToken, 'base64').toString()) }
            : {}),
        }),
      )

      return {
        items: (result.Items ?? [])
          .map(raw => unmarshall(raw))
          .map(item => ({
            id: item.TransactionId as string,
            type: item.Type as string,
            status: item.Status as string,
            points: Number(item.Points),
            createdAt: `${(item.CreatedAt as string).slice(0, 23)}Z`,
            orderId: (item.OrderId as string) ?? null,
          })),
        nextToken: result.LastEvaluatedKey
          ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
          : null,
      }
    },

    // Mirrors the constants baked into the rewardConversion NONE resolver in prod
    // (infra/constructs/reward-config.ts) and the Pricing Lambdas' Rewards__* env vars
    // (PricingExtensions.cs) — kept in sync by hand, since local dev has no CDK synth step
    // to read them from (ADR-0048 §3).
    async rewardConversion() {
      return { pointsPerUnit: 100, currencyPerUnit: 1 }
    },

    // Mirrors Query.warehouses.js (StockKeeper/Admin in prod; no Cognito groups locally).
    async warehouses() {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: WAREHOUSES_TABLE,
          FilterExpression: '#type = :warehouse',
          ExpressionAttributeNames: { '#type': 'Type' },
          ExpressionAttributeValues: { ':warehouse': { S: 'Warehouse' } },
        }),
      )
      if (result.LastEvaluatedKey) {
        throw createGraphQLError('The warehouse list is larger than one Scan page and was truncated', {
          extensions: { errorType: 'Truncated' },
        })
      }
      return (result.Items ?? [])
        .map(item => mapWarehouse(unmarshall(item)))
        .sort((a, b) => a.priority - b.priority || (a.code < b.code ? -1 : a.code > b.code ? 1 : 0))
    },

    // Mirrors Query.warehouse.js: null for an unknown id or a CODE# guard.
    async warehouse(_: unknown, { id }: { id: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: WAREHOUSES_TABLE, Key: { WarehouseId: { S: id } } }),
      )
      const item = result.Item ? unmarshall(result.Item) : null
      return item?.Type === 'Warehouse' ? mapWarehouse(item) : null
    },
  },

  Mutation: {
    async storeBasket(
      _: unknown,
      { ownerId, input }: { ownerId: string; input: { items: Array<Record<string, unknown>> } },
    ) {
      // Mirrors the AppSync direct PutItem resolver (ADR-0009) — cart JSON blob shape must match
      // Basket.Function's ShoppingCartSerializer contract exactly.
      const items = input.items.map(i => ({
        Quantity: i.quantity,
        Color: (i.color as string) ?? '',
        Price: i.price,
        ProductId: i.productId,
        ProductName: i.productName,
        // Main image key snapshot (ADR-0034) — a key, never a URL; null before the pipeline.
        ImageId: (i.imageId as string) ?? null,
      }))
      const totalPrice = items.reduce(
        (sum, item) => sum + (item.Price as number) * (item.Quantity as number),
        0,
      )
      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'shopping-carts',
          Item: {
            OwnerId: { S: ownerId },
            Data: { S: JSON.stringify({ OwnerId: ownerId, Items: items, TotalPrice: totalPrice }) },
            // Same as the resolver: a fresh token per write, checked by the checkout (ADR-0050 §4).
            Version: { S: randomUUID() },
            // Only guest carts expire; user carts omit ExpiresAt so DynamoDB TTL never touches them.
            ...(ownerId.startsWith('GUEST#')
              ? { ExpiresAt: { N: String(Math.floor(Date.now() / 1000) + 15 * 24 * 60 * 60) } }
              : {}),
          },
        }),
      )
      return { ownerId }
    },

    async checkoutBasket(
      _: unknown,
      { input }: { input: Record<string, unknown> },
      context: LocalContext,
    ) {
      // Mirrors the checkout-saga state machine step by step (ADR-0050 §9): same OrderId formula,
      // same order of checks, same errorTypes. Checkout is Cognito-only in prod (the resolver derives
      // the owner from the token); locally the owner comes from the BFF-resolved context.
      const ownerId = context.owner.ownerId
      if (ownerId.startsWith('GUEST#')) throw unauthorized('checkoutBasket')
      const customerId = customerIdFromOwner(ownerId)

      // The resolver's input checks (Mutation.checkoutBasket.js), refused before the saga runs.
      const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
      if (!UUID.test(String(input.checkoutId ?? ''))) {
        throw refusal('BadRequest', 'checkoutId must be a UUID.')
      }
      if (!String(input.addressLine ?? '').trim()) throw refusal('BadRequest', 'An address is required.')
      if (!String(input.emailAddress ?? '').trim()) throw refusal('BadRequest', 'An email address is required.')
      if (![1, 2, 3].includes(Number(input.paymentMethod))) throw refusal('BadRequest', 'Unknown payment method.')
      if (Number(input.installments) < 1) throw refusal('BadRequest', 'installments must be at least 1.')

      const checkoutId = String(input.checkoutId)
      const orderId = checkoutOrderId(ownerId, checkoutId)

      // LoadCheckoutRecord: a replay of this attempt answers with the order it already created.
      const record = await dynamoDb.send(
        new GetItemCommand({
          TableName: 'shopping-carts',
          Key: { OwnerId: { S: `CHECKOUT#${ownerId}#${checkoutId}` } },
          ConsistentRead: true,
        }),
      )
      if (record.Item) return { isSuccess: true, orderId: record.Item.OrderId?.S ?? null }

      // LoadCart: only ProductId and Quantity are taken from the client's cart (ADR-0050 §5).
      const cart = await dynamoDb.send(
        new GetItemCommand({ TableName: 'shopping-carts', Key: { OwnerId: { S: ownerId } }, ConsistentRead: true }),
      )
      const items = cart.Item
        ? ((JSON.parse(cart.Item.Data?.S ?? '{}').Items ?? []) as Array<{ ProductId: string; Quantity: number }>)
            .map(i => ({ ProductId: String(i.ProductId).toLowerCase(), Quantity: Number(i.Quantity) }))
        : []
      if (items.length === 0) throw refusal('BadRequest', 'There is no cart to check out.')
      const cartVersion = cart.Item?.Version?.S ?? null
      const productIds = [...new Set(items.map(i => i.ProductId))]
      if (productIds.length > 100) throw refusal('BadRequest', 'A cart can hold at most 100 different products.')

      // CatalogLines: name and main image from Catalog.
      const catalogResult = await dynamoDb.send(
        new BatchGetItemCommand({
          RequestItems: {
            products: {
              Keys: productIds.map(id => ({ Id: { S: id } })),
              ProjectionExpression: 'Id, #name, Images',
              ExpressionAttributeNames: { '#name': 'Name' },
            },
          },
        }),
      )
      if (Object.keys(catalogResult.UnprocessedKeys ?? {}).length > 0) throw internalError()
      const catalog = new Map(
        (catalogResult.Responses?.products ?? []).map(p => {
          const product = unmarshall(p) as { Id: string; Name: string; Images?: Array<{ ImageId: string; IsMain: boolean }> }
          return [product.Id, { name: product.Name, imageId: product.Images?.find(i => i.IsMain)?.ImageId ?? null }]
        }),
      )
      const missing = productIds.filter(id => !catalog.has(id))
      if (missing.length > 0) {
        throw refusal('ProductUnavailable', 'Some products in your cart are no longer available.', { productIds: missing })
      }

      // Quote: Pricing's charged total and per-line prices, or PriceChanged / PointsAboveCap.
      const pointsToUse = Number(input.pointsToUse ?? 0)
      const quote = await quoteBasket({
        Items: items,
        PointsToUse: pointsToUse,
        ExpectedTotal: input.totalPrice,
        PaymentMethod: input.paymentMethod,
        Installments: input.installments,
      })

      // On AWS a missing balance only surfaces after checkout (Failed reservation → payment
      // declined). Locally there's no event flow to decline the order, so refuse up front.
      if (pointsToUse > 0 && (await pointsBalance(ownerId)) < pointsToUse) {
        throw refusal('InsufficientPoints', 'Not enough points for this order.')
      }

      // Never write an order without one Pricing price per cart line.
      if ((quote.Lines ?? []).length !== items.length) throw internalError()

      // RegisterPayment: the card reaches Payment here and nowhere else (ADR-0050 §6/§7).
      const registration = await invokeLambda<{
        PaymentId?: string
        Refusal?: { ErrorType: string; Message: string; Field?: string }
      }>('payment-register', {
        OrderId: orderId,
        CustomerId: customerId,
        Amount: quote.ChargedTotal,
        PaymentMethod: input.paymentMethod,
        Installments: input.installments,
        CardName: input.cardName ?? null,
        CardNumber: input.cardNumber ?? null,
        Expiration: input.expiration ?? null,
        Cvv: input.cvv ?? null,
      })
      if (registration.Refusal) {
        throw refusal(registration.Refusal.ErrorType, registration.Refusal.Message, { field: registration.Refusal.Field })
      }

      // Checkout: Basket closes the cart atomically with the lines assembled here.
      const lines = quote.Lines ?? []
      const body = await invokeLambda<{ IsSuccess: boolean; OrderId: string | null; ErrorType?: string }>(
        'basket-checkout-basket',
        {
          CheckoutId: checkoutId,
          CartVersion: cartVersion,
          BasketCheckoutDto: {
            OwnerId: ownerId,
            CustomerId: customerId,
            OrderId: orderId,
            TotalPrice: quote.ChargedTotal,
            PointsToUse: pointsToUse,
            ShippingAddress: {
              FirstName: input.firstName,
              LastName: input.lastName,
              EmailAddress: input.emailAddress,
              AddressLine: input.addressLine,
              Country: input.country,
              State: input.state,
              ZipCode: input.zipCode,
            },
            // No card: it was registered with Payment above.
            Payment: {
              PaymentMethod: input.paymentMethod,
              Installments: input.installments,
            },
            Items: items.map((item, index) => ({
              ProductId: item.ProductId,
              ProductName: catalog.get(item.ProductId)?.name,
              ImageId: catalog.get(item.ProductId)?.imageId,
              Quantity: item.Quantity,
              Price: lines[index]?.UnitPrice,
            })),
          },
        },
      )
      if (body.ErrorType === 'CartChanged') {
        // Compensation, only for a typed refusal: the cart provably did not close.
        await invokeLambda('payment-void', { OrderId: orderId })
        throw refusal('CartChanged', 'Your cart changed. Review it before checking out.')
      }
      if (!body.IsSuccess || body.OrderId !== orderId) throw internalError()

      await createEligibleReviews(customerId, productIds)
      if (pointsToUse > 0 && body.OrderId) await redeemPointsForOrder(ownerId, body.OrderId, pointsToUse)
      return { isSuccess: true, orderId: body.OrderId ?? null }
    },

    async mergeBasket(
      _: unknown,
      { guestId }: { guestId: string },
      context: LocalContext,
    ) {
      const body = await invokeLambda<{ OwnerId: string }>('basket-merge-basket', {
        OwnerId: context.owner.ownerId,
        GuestId: guestId,
      })
      return { ownerId: body.OwnerId }
    },

    async deleteBasket(_: unknown, { ownerId }: { ownerId: string }) {
      await dynamoDb.send(
        new DeleteItemCommand({
          TableName: 'shopping-carts',
          Key: { OwnerId: { S: ownerId } },
        }),
      )
      return { isSuccess: true }
    },

    async updateProfile(
      _: unknown,
      { input }: { input: Record<string, string | null | undefined> },
      context: LocalContext,
    ) {
      const userId = customerIdFromOwner(context.owner.ownerId)

      const sets = ['Email = :email', '#name = :name']
      const names: Record<string, string> = { '#name': 'Name' }
      const values: Record<string, { S: string }> = {
        ':email': { S: `${userId}@local.dev` },
        ':name': { S: String(input.name) },
      }
      const optional: Array<[string, string, string?]> = [
        ['Phone', 'phone'],
        ['AddressLine', 'addressLine'],
        ['City', 'city'],
        ['State', 'state', '#state'],
        ['ZipCode', 'zipCode'],
        ['Country', 'country'],
      ]
      for (const [attr, key, alias] of optional) {
        const v = input[key]
        if (v != null) {
          const name = alias ?? attr
          if (alias) names[alias] = attr
          sets.push(`${name} = :${key}`)
          values[`:${key}`] = { S: v }
        }
      }

      const result = await dynamoDb.send(
        new UpdateItemCommand({
          TableName: 'user-profiles',
          Key: { UserId: { S: userId } },
          UpdateExpression: 'SET ' + sets.join(', '),
          ExpressionAttributeNames: names,
          ExpressionAttributeValues: values,
          ReturnValues: 'ALL_NEW',
        }),
      )

      const item = unmarshall(result.Attributes ?? {})
      return {
        userId: item.UserId as string,
        email: item.Email as string,
        name: item.Name as string,
        phone: (item.Phone as string) ?? null,
        addressLine: (item.AddressLine as string) ?? null,
        city: (item.City as string) ?? null,
        state: (item.State as string) ?? null,
        zipCode: (item.ZipCode as string) ?? null,
        country: (item.Country as string) ?? null,
      }
    },

    async createProductImageUpload(
      _: unknown,
      { input }: { input: { contentTypes: string[] } },
    ) {
      // There is no local S3 (ADR-0034: dev/test run against real AWS) — presign against the
      // real dev originals bucket using ambient credentials. Dynamic imports keep the S3 SDK
      // out of the module graph for everyone not using the admin upload flow locally.
      const bucket = process.env.IMAGE_ORIGINALS_BUCKET
      if (!bucket) {
        throw createGraphQLError(
          'Image upload in local dev needs IMAGE_ORIGINALS_BUCKET in .env.local plus real AWS credentials (ADR-0034).',
        )
      }
      const [{ S3Client }, { createPresignedPost }, { ulid }] = await Promise.all([
        import('@aws-sdk/client-s3'),
        import('@aws-sdk/s3-presigned-post'),
        import('ulid'),
      ])
      // Deliberately NOT the DynamoDB Local credentials above — this client talks to real AWS.
      const s3 = new S3Client({ region: process.env.IMAGE_AWS_REGION ?? 'us-east-1' })

      const extensions: Record<string, string> = {
        'image/jpeg': 'jpg',
        'image/png': 'png',
        'image/webp': 'webp',
        'image/avif': 'avif',
      }
      return Promise.all(
        input.contentTypes.map(async contentType => {
          const ext = extensions[contentType]
          if (!ext) throw createGraphQLError(`Unsupported content type "${contentType}".`)
          const imageId = ulid()
          const { url, fields } = await createPresignedPost(s3, {
            Bucket: bucket,
            Key: `images/${imageId}/original.${ext}`,
            Fields: { 'Content-Type': contentType },
            Conditions: [['content-length-range', 1, 8 * 1024 * 1024]],
            Expires: 15 * 60,
          })
          return { imageId, url, fields: JSON.stringify(fields) }
        }),
      )
    },

    async createProduct(
      _: unknown,
      { input }: { input: Record<string, unknown> },
    ) {
      const id = randomUUID()
      // Price is set separately via setNominalPrice (ADR-0026) — Catalog no longer stores it.
      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'products',
          Item: {
            Id: { S: id },
            Name: { S: input.name as string },
            Description: { S: input.description as string },
            Images: marshalImages(input.images as ProductImageInput[]),
            Stock: { N: String(input.stock) },
            CategoryIds: { SS: input.categoryIds as string[] },
          },
        }),
      )
      return { id }
    },

    async createProductWithPrice(
      _: unknown,
      { input }: { input: Record<string, unknown> },
    ) {
      // Simulates the production Step Functions Express saga (ADR-0032): product write,
      // price write, and a compensating delete when the price write fails.
      const id = randomUUID()
      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'products',
          Item: {
            Id: { S: id },
            Name: { S: input.name as string },
            Description: { S: input.description as string },
            Images: marshalImages(input.images as ProductImageInput[]),
            Stock: { N: String(input.stock) },
            CategoryIds: { SS: input.categoryIds as string[] },
          },
        }),
      )
      try {
        await dynamoDb.send(
          new UpdateItemCommand({
            TableName: 'prices',
            Key: { ProductId: { S: id } },
            UpdateExpression: 'SET NominalPrice = :nominalPrice, Cost = :cost, UpdatedAt = :now',
            ExpressionAttributeValues: {
              ':nominalPrice': { N: String(input.price) },
              ':cost': { N: String(input.cost) },
              ':now': { S: new Date().toISOString() },
            },
          }),
        )
      } catch {
        await dynamoDb.send(
          new DeleteItemCommand({ TableName: 'products', Key: { Id: { S: id } } }),
        )
        throw createGraphQLError('Price write failed; product creation rolled back', {
          extensions: { code: 'SAGA_FAILED' },
        })
      }
      return { id }
    },

    async updateProduct(
      _: unknown,
      { input }: { input: Record<string, unknown> },
    ) {
      try {
        await dynamoDb.send(
          new UpdateItemCommand({
            TableName: 'products',
            Key: { Id: { S: input.id as string } },
            UpdateExpression:
              'SET #Name = :name, Description = :desc, Images = :imgs, Stock = :stock, CategoryIds = :cats',
            ExpressionAttributeNames: { '#Name': 'Name' },
            ExpressionAttributeValues: {
              ':name': { S: input.name as string },
              ':desc': { S: input.description as string },
              ':imgs': marshalImages(input.images as ProductImageInput[]),
              ':stock': { N: String(input.stock) },
              ':cats': { SS: input.categoryIds as string[] },
            },
            ConditionExpression: 'attribute_exists(Id)',
          }),
        )
        return { id: input.id }
      } catch (err) {
        if (err instanceof ConditionalCheckFailedException) {
          throw createGraphQLError(`Product ${input.id as string} not found`, {
            extensions: { code: 'NOT_FOUND' },
          })
        }
        throw err
      }
    },

    // Mirrors Mutation.updateChallengePoints.js (Admin-only in prod; no Cognito groups locally, same
    // as the other admin mutations here): UpdateItem on the PUBLIC item only, so the new value
    // applies to future answers; past attempts and the points ledger are untouched (ADR-0045 §2).
    async updateChallengePoints(_: unknown, { id, points }: { id: string; points: number }) {
      if (!Number.isInteger(points) || points <= 0 || points > MAX_CHALLENGE_POINTS) {
        throw createGraphQLError(`points must be an integer between 1 and ${MAX_CHALLENGE_POINTS}`, {
          extensions: { errorType: 'BadRequest' },
        })
      }
      try {
        const result = await dynamoDb.send(
          new UpdateItemCommand({
            TableName: 'challenges',
            Key: { QuestionId: { S: id }, SK: { S: 'PUBLIC' } },
            UpdateExpression: 'SET Points = :points, UpdatedAt = :now',
            // Without this, UpdateItem would upsert a phantom PUBLIC item for an unknown id.
            ConditionExpression: 'attribute_exists(QuestionId)',
            ExpressionAttributeValues: {
              ':points': { N: String(points) },
              ':now': { S: new Date().toISOString() },
            },
            ReturnValues: 'ALL_NEW',
          }),
        )
        return mapChallenge(unmarshall(result.Attributes ?? {}))
      } catch (err) {
        if (err instanceof ConditionalCheckFailedException) {
          throw createGraphQLError('Challenge not found', { extensions: { errorType: 'NOT_FOUND' } })
        }
        throw err
      }
    },

    // Mirrors Mutation.createWarehouse.js: the warehouse and its CODE#<code> guard in one
    // transaction, both attribute_not_exists (ADR-0052 §2).
    async createWarehouse(_: unknown, { input }: { input: WarehouseFieldsInput & { code: string } }) {
      const code = warehouseValidated(normalizeWarehouseCode(input.code))
      const fields = warehouseValidated(normalizeWarehouseFields(input))
      const id = randomUUID()
      const now = new Date().toISOString()
      const item: Record<string, AttributeValue> = {
        WarehouseId: { S: id },
        Type: { S: 'Warehouse' },
        Code: { S: code },
        ...warehouseAttributes(fields),
        Active: { BOOL: true },
        CreatedAt: { S: now },
        UpdatedAt: { S: now },
        UpdatedBy: { S: LOCAL_WAREHOUSE_AUTHOR },
      }
      try {
        await dynamoDb.send(
          new TransactWriteItemsCommand({
            TransactItems: [
              { Put: { TableName: WAREHOUSES_TABLE, Item: item, ConditionExpression: 'attribute_not_exists(WarehouseId)' } },
              {
                Put: {
                  TableName: WAREHOUSES_TABLE,
                  Item: { WarehouseId: { S: `CODE#${code}` }, Type: { S: 'CodeGuard' }, TargetWarehouseId: { S: id } },
                  ConditionExpression: 'attribute_not_exists(WarehouseId)',
                },
              },
            ],
          }),
        )
      } catch (err) {
        if (err instanceof TransactionCanceledException) {
          const reasons = err.CancellationReasons ?? []
          if (reasons[1]?.Code === 'ConditionalCheckFailed') {
            throw createGraphQLError(`Warehouse code ${code} is already taken`, {
              extensions: { errorType: 'WarehouseCodeTaken', errorInfo: { field: 'code' } },
            })
          }
          if (reasons.some(reason => reason.Code === 'TransactionConflict')) {
            throw createGraphQLError('Another write to this warehouse code is in progress; retry', {
              extensions: { errorType: 'TransactionConflict' },
            })
          }
        }
        throw err
      }
      return mapWarehouse(unmarshall(item))
    },

    // Mirrors Mutation.updateWarehouse.js: name, address and priority; the code is immutable.
    async updateWarehouse(_: unknown, { input }: { input: WarehouseFieldsInput & { id: string } }) {
      const fields = warehouseValidated(normalizeWarehouseFields(input))
      const attributes = warehouseAttributes(fields)
      const values: Record<string, AttributeValue> = {
        ':name': attributes.Name,
        ':street': attributes.Street,
        ':number': attributes.Number,
        ':district': attributes.District,
        ':city': attributes.City,
        ':state': attributes.State,
        ':zipCode': attributes.ZipCode,
        ':priority': attributes.Priority,
        ':now': { S: new Date().toISOString() },
        ':by': { S: LOCAL_WAREHOUSE_AUTHOR },
        ':warehouse': { S: 'Warehouse' },
      }
      let expression = 'SET #name = :name, Street = :street, #number = :number, District = :district, City = :city, '
        + '#state = :state, ZipCode = :zipCode, Priority = :priority, UpdatedAt = :now, UpdatedBy = :by'
      if (attributes.Complement) {
        expression += ', Complement = :complement'
        values[':complement'] = attributes.Complement
      } else {
        expression += ' REMOVE Complement'
      }
      try {
        const result = await dynamoDb.send(
          new UpdateItemCommand({
            TableName: WAREHOUSES_TABLE,
            Key: { WarehouseId: { S: input.id } },
            UpdateExpression: expression,
            ConditionExpression: 'attribute_exists(WarehouseId) AND #type = :warehouse',
            ExpressionAttributeNames: { '#name': 'Name', '#number': 'Number', '#state': 'State', '#type': 'Type' },
            ExpressionAttributeValues: values,
            ReturnValues: 'ALL_NEW',
          }),
        )
        return mapWarehouse(unmarshall(result.Attributes ?? {}))
      } catch (err) {
        if (err instanceof ConditionalCheckFailedException) throw warehouseNotFound()
        throw err
      }
    },

    // Mirrors Mutation.setWarehouseActive.js: deactivation instead of delete (ADR-0052 §2).
    async setWarehouseActive(_: unknown, { id, active }: { id: string; active: boolean }) {
      try {
        const result = await dynamoDb.send(
          new UpdateItemCommand({
            TableName: WAREHOUSES_TABLE,
            Key: { WarehouseId: { S: id } },
            UpdateExpression: 'SET Active = :active, UpdatedAt = :now, UpdatedBy = :by',
            ConditionExpression: 'attribute_exists(WarehouseId) AND #type = :warehouse',
            ExpressionAttributeNames: { '#type': 'Type' },
            ExpressionAttributeValues: {
              ':active': { BOOL: active },
              ':now': { S: new Date().toISOString() },
              ':by': { S: LOCAL_WAREHOUSE_AUTHOR },
              ':warehouse': { S: 'Warehouse' },
            },
            ReturnValues: 'ALL_NEW',
          }),
        )
        return mapWarehouse(unmarshall(result.Attributes ?? {}))
      } catch (err) {
        if (err instanceof ConditionalCheckFailedException) throw warehouseNotFound()
        throw err
      }
    },

    async setNominalPrice(
      _: unknown,
      { productId, price, cost }: { productId: string; price: number; cost: number },
    ) {
      // Mirrors the AppSync direct UpdateItem resolver (ADR-0009) — the `prices` item only ever
      // persists a single UpdatedAt timestamp, so a plain upsert needs no prior read.
      const now = new Date().toISOString()
      const result = await dynamoDb.send(
        new UpdateItemCommand({
          TableName: 'prices',
          Key: { ProductId: { S: productId } },
          UpdateExpression: 'SET NominalPrice = :nominalPrice, Cost = :cost, UpdatedAt = :now',
          ExpressionAttributeValues: {
            ':nominalPrice': { N: String(price) },
            ':cost': { N: String(cost) },
            ':now': { S: now },
          },
          ReturnValues: 'ALL_NEW',
        }),
      )
      const item = unmarshall(result.Attributes ?? {})
      return {
        productId: item.ProductId,
        nominalPrice: Number(item.NominalPrice),
        cost: Number(item.Cost),
        updatedAt: item.UpdatedAt,
      }
    },

    async setGatewayCost(
      _: unknown,
      args: {
        provider: string
        flatFeePerTransaction: number
        avistaRatePercent: number
        installmentRates: Record<string, number>
      },
    ) {
      // Mirrors the AppSync direct UpdateItem resolver (ADR-0009) — same single-UpdatedAt-
      // attribute contract as `prices`, so a plain upsert needs no prior read.
      const now = new Date().toISOString()
      const result = await dynamoDb.send(
        new UpdateItemCommand({
          TableName: 'gateway-costs',
          Key: { Provider: { S: args.provider } },
          UpdateExpression:
            'SET FlatFeePerTransaction = :flatFee, AvistaRatePercent = :avista, InstallmentRates = :rates, UpdatedAt = :now',
          ExpressionAttributeValues: {
            ':flatFee': { N: String(args.flatFeePerTransaction) },
            ':avista': { N: String(args.avistaRatePercent) },
            ':rates': {
              M: Object.fromEntries(
                Object.entries(args.installmentRates).map(([k, v]) => [k, { N: String(v) }]),
              ),
            },
            ':now': { S: now },
          },
          ReturnValues: 'ALL_NEW',
        }),
      )
      const item = unmarshall(result.Attributes ?? {})
      return {
        provider: item.Provider,
        flatFeePerTransaction: Number(item.FlatFeePerTransaction),
        avistaRatePercent: Number(item.AvistaRatePercent),
        installmentRates: item.InstallmentRates,
      }
    },

    async createCampaign(
      _: unknown,
      args: {
        name: string
        discountType: string
        value: number
        startsAt: string
        endsAt: string
        productIds: string[]
      },
    ) {
      const body = await invokeLambda<{ Id: string }>('pricing-create-campaign', {
        Name: args.name,
        DiscountType: args.discountType,
        Value: args.value,
        StartsAt: args.startsAt,
        EndsAt: args.endsAt,
        ProductIds: args.productIds,
      })
      return {
        id: body.Id,
        name: args.name,
        discountType: args.discountType,
        value: args.value,
        startsAt: args.startsAt,
        endsAt: args.endsAt,
        productIds: args.productIds,
        // A freshly created campaign is always Active — Cancelled only happens via endCampaign.
        status: 'Active',
      }
    },

    async endCampaign(_: unknown, { campaignId }: { campaignId: string }) {
      const body = await invokeLambda<{ Ended: boolean }>('pricing-end-campaign', {
        CampaignId: campaignId,
      })
      return body.Ended
    },

    async deleteProduct(_: unknown, { id }: { id: string }) {
      await dynamoDb.send(
        new DeleteItemCommand({
          TableName: 'products',
          Key: { Id: { S: id } },
        }),
      )
      return { isSuccess: true }
    },

    // Direct DynamoDB DeleteItem (ADR-0009) — mirrors the AppSync deleteOrder resolver.
    async deleteOrder(_: unknown, { orderId }: { orderId: string }) {
      await dynamoDb.send(
        new DeleteItemCommand({ TableName: 'ordering', Key: { Id: { S: orderId } } }),
      )
      return { isSuccess: true }
    },

    // Mirrors the AppSync pipeline (Mutation.createReview.checkExisting.js → .upsert.js), since
    // AppSync JS resolvers don't run locally. The row must already exist — created Eligible at
    // purchase time (locally by checkoutBasket below) — so a customer who never bought the product
    // gets Unauthorized (ADR-0049 §4). The write is an UpdateItem that publishes the row: Status =
    // Published, GSI1 set (sparse index → visible in reviewsByProduct), ExpiresAt removed for a
    // re-publish after a delete. CreatedAt is preserved so GSI1SK never moves. UserId comes from
    // the BFF-resolved owner, mirroring ctx.identity.sub; UserName is a placeholder derived from
    // the userId (no Cognito claim to read locally — same limitation as myProfile/local checkout).
    async createReview(
      _: unknown,
      { input }: { input: { productId: string; rating: number; comment: string } },
      context: LocalContext,
    ) {
      const userId = customerIdFromOwner(context.owner.ownerId)
      const userName = userId
      const id = `${input.productId}#${userId}`

      const existing = await dynamoDb.send(
        new GetItemCommand({ TableName: 'reviews', Key: { Id: { S: id } } }),
      )
      if (!existing.Item) throw unauthorized('createReview')

      const now = new Date().toISOString()
      const previous = unmarshall(existing.Item)
      const createdAt = (previous.CreatedAt as string | undefined) ?? now

      try {
        await dynamoDb.send(
          new UpdateItemCommand({
            TableName: 'reviews',
            Key: { Id: { S: id } },
            UpdateExpression:
              'SET #status = :published, Rating = :rating, #comment = :comment, UserName = :userName, ' +
              'UserId = :userId, CreatedAt = :createdAt, UpdatedAt = :updatedAt, ' +
              'GSI1PK = :productId, GSI1SK = :createdAt REMOVE ExpiresAt',
            ConditionExpression: 'attribute_exists(Id)',
            ExpressionAttributeNames: { '#status': 'Status', '#comment': 'Comment' },
            ExpressionAttributeValues: {
              ':published': { S: 'Published' },
              ':rating': { N: String(input.rating) },
              ':comment': { S: input.comment },
              ':userName': { S: userName },
              ':userId': { S: userId },
              ':createdAt': { S: createdAt },
              ':updatedAt': { S: now },
              ':productId': { S: input.productId },
            },
          }),
        )
      } catch (err) {
        // The row expired (TTL) between the GetItem and the update.
        if (err instanceof ConditionalCheckFailedException) throw unauthorized('createReview')
        throw err
      }

      // On AWS the transition to Published emits ReviewCreatedEvent and
      // challenges-review-created-consumer credits the points; locally EventBridge is best-effort,
      // so the credit is simulated here, with the same transaction and the same once-per-product
      // REVIEW#<productId> lock (ADR-0048). Legacy rows without Status already count as
      // Published, so they never credit.
      const wasPublished = previous.Status === undefined || previous.Status === 'Published'
      if (!wasPublished) await creditReviewPoints(context.owner.ownerId, input.productId)

      return { id, userName }
    },

    // Mirrors Mutation.deleteReview.js: withdraws a Published (or legacy, Status-less) review —
    // Status = Deleted, GSI1 removed (gone from reviewsByProduct) and ExpiresAt = now + 5 days, so
    // the TTL erases the row and the customer can re-publish only until then (ADR-0049 §4, §7).
    async deleteReview(_: unknown, { productId }: { productId: string }, context: LocalContext) {
      const userId = customerIdFromOwner(context.owner.ownerId)
      try {
        const result = await dynamoDb.send(
          new UpdateItemCommand({
            TableName: 'reviews',
            Key: { Id: { S: `${productId}#${userId}` } },
            UpdateExpression:
              'SET #status = :deleted, ExpiresAt = :expiresAt, UpdatedAt = :updatedAt REMOVE GSI1PK, GSI1SK',
            // attribute_exists(Id) first: on a missing item attribute_not_exists(Status) is true,
            // and the UpdateItem would create a Deleted row for a product never bought.
            ConditionExpression:
              'attribute_exists(Id) AND (#status = :published OR attribute_not_exists(#status))',
            ExpressionAttributeNames: { '#status': 'Status' },
            ExpressionAttributeValues: {
              ':deleted': { S: 'Deleted' },
              ':published': { S: 'Published' },
              ':expiresAt': { N: String(Math.floor(Date.now() / 1000) + REVIEW_TTL_SECONDS) },
              ':updatedAt': { S: new Date().toISOString() },
            },
            ReturnValues: 'ALL_NEW',
          }),
        )
        return mapReview(unmarshall(result.Attributes ?? {}))
      } catch (err) {
        // No row, or not Published: there is no public review of this customer to delete.
        if (err instanceof ConditionalCheckFailedException) {
          throw createGraphQLError('No published review to delete', {
            extensions: { errorType: 'DynamoDB:ConditionalCheckFailedException' },
          })
        }
        throw err
      }
    },

    // Cognito-only in prod (ADR-0045 §3): grading happens server-side, so this still goes through
    // the Challenges Lambda even locally rather than being reimplemented against DynamoDB here —
    // the whole point of ADR-0045 is that only that Lambda ever reads the ANSWER item.
    async submitChallengeAnswer(
      _: unknown,
      { challengeId, selectedOption }: { challengeId: string; selectedOption: number },
      context: LocalContext,
    ) {
      const body = await invokeLambda<{
        IsCorrect: boolean
        PointsEarned: number
        NewScore: number
        Explanation: string
        SelectedOption: number
      }>('challenges-submit-answer', {
        OwnerId: context.owner.ownerId,
        ChallengeId: challengeId,
        SelectedOption: selectedOption,
      })
      return {
        isCorrect: body.IsCorrect,
        pointsEarned: body.PointsEarned,
        newScore: body.NewScore,
        explanation: body.Explanation,
        // Echoed from the stored attempt, which on a re-submission is the *first* answer, not the
        // one just sent (ADR-0045 §4).
        selectedOption: body.SelectedOption,
      }
    },

    async revealChallengeHint(
      _: unknown,
      { challengeId }: { challengeId: string },
      context: LocalContext,
    ) {
      const body = await invokeLambda<{ Hint: string; HintsRevealed: number; PenaltyApplied: number }>(
        'challenges-reveal-hint',
        { OwnerId: context.owner.ownerId, ChallengeId: challengeId },
      )
      return {
        hint: body.Hint,
        hintsRevealed: body.HintsRevealed,
        penaltyApplied: body.PenaltyApplied,
      }
    },
  },
}

const yoga = createYoga<LocalContext>({
  schema: createSchema({ typeDefs, resolvers }),
  graphqlEndpoint: '/api/graphql',
  maskedErrors: true,
})

export async function handleLocal(request: Request): Promise<Response> {
  // Same BFF identity path as prod: inject the resolved ownerId into basket operations and
  // expose the owner to the Yoga resolvers (checkoutBasket/mergeBasket read it from context).
  const { body, owner, setGuestCookie } = await prepareBasketRequest(await request.text())
  const forwarded = new Request(request.url, {
    method: request.method,
    headers: request.headers,
    body,
  })

  const response = await yoga.handleRequest(forwarded, { owner })

  if (setGuestCookie) response.headers.append('Set-Cookie', guestCookieHeader(owner))
  return response
}
