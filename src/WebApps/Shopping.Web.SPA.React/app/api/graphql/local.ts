import { createYoga, createSchema, createGraphQLError } from 'graphql-yoga'
import { GraphQLScalarType, Kind } from 'graphql'
import {
  DynamoDBClient,
  ScanCommand,
  QueryCommand,
  GetItemCommand,
  PutItemCommand,
  UpdateItemCommand,
  DeleteItemCommand,
  ConditionalCheckFailedException,
} from '@aws-sdk/client-dynamodb'
import { LambdaClient, InvokeCommand } from '@aws-sdk/client-lambda'
import { unmarshall } from '@aws-sdk/util-dynamodb'
import { randomUUID } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { prepareBasketRequest, guestCookieHeader } from '@/lib/basket-bff'
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
      cardName: (pay.CardName as string) ?? '',
      cardNumber: (pay.CardNumber as string) ?? '',
      expiration: (pay.Expiration as string) ?? '',
      cvv: (pay.Cvv as string) ?? '',
      paymentMethod: (pay.PaymentMethod as number) ?? 0,
      installments: (pay.Installments as number) ?? 1,
    },
    orderItems: rawItems.map(oi => ({
      productId: String(oi.ProductId ?? ''),
      quantity: Number(oi.Quantity ?? 0),
      price: Number(oi.Price ?? 0),
    })),
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
      const expressionValues: Record<string, unknown> = {}
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

    // Sums cost/originalPrice across every cart item, then runs the whole cart through the same
    // cost-floor calculation as a single checkout transaction (ADR-0009).
    async basketInstallmentPlan(
      _: unknown,
      { items }: { items: Array<{ productId: string; quantity: number }> },
    ) {
      const body = await invokeLambda<{
        TotalOriginalPrice: number
        Price: number
        CashPrice: number
        MaxInstallmentsWithoutInterest: number
        InstallmentPlan: { Count: number; Value: number; TotalValue: number; HasInterest: boolean }[]
      }>('pricing-get-basket-installment-plan', {
        Items: items.map(i => ({ ProductId: i.productId, Quantity: i.quantity })),
      })
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
      }
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

      const items = (result.Items ?? []).map(raw => {
        const item = unmarshall(raw)
        return {
          id: item.Id as string,
          productId: item.ProductId as string,
          userName: item.UserName as string,
          rating: Number(item.Rating),
          comment: item.Comment as string,
          createdAt: item.CreatedAt as string,
          updatedAt: item.UpdatedAt as string,
        }
      })

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
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
      // Checkout is Cognito-only in prod (resolver derives the owner from the token). Locally
      // there is no Cognito, so we take the owner from the BFF-resolved context.
      const ownerId = context.owner.ownerId
      const body = await invokeLambda<{ IsSuccess: boolean }>('basket-checkout-basket', {
        BasketCheckoutDto: {
          OwnerId: ownerId,
          CustomerId: customerIdFromOwner(ownerId),
          TotalPrice: input.totalPrice,
          FirstName: input.firstName,
          LastName: input.lastName,
          EmailAddress: input.emailAddress,
          AddressLine: input.addressLine,
          Country: input.country,
          State: input.state,
          ZipCode: input.zipCode,
          // Optional — empty for Cash, which carries no card.
          CardName: input.cardName ?? '',
          CardNumber: input.cardNumber ?? '',
          Expiration: input.expiration ?? '',
          Cvv: input.cvv ?? '',
          PaymentMethod: input.paymentMethod,
          Installments: input.installments,
        },
      })
      return { isSuccess: body.IsSuccess }
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

    // Upserts by composite Id `${productId}#${base64(userName)}` (ADR-0029) — the same key the
    // AppSync JS pipeline resolver computes in production, since AppSync JS resolvers don't run
    // locally (see CLAUDE.md). CreatedAt is preserved from the existing item on an edit so GSI1SK
    // never moves; UpdatedAt is always refreshed.
    async createReview(
      _: unknown,
      { input }: { input: { productId: string; userName: string; rating: number; comment: string } },
    ) {
      const id = `${input.productId}#${Buffer.from(input.userName, 'utf-8').toString('base64')}`

      const existing = await dynamoDb.send(
        new GetItemCommand({ TableName: 'reviews', Key: { Id: { S: id } } }),
      )
      const createdAt = existing.Item ? unmarshall(existing.Item).CreatedAt as string : new Date().toISOString()
      const updatedAt = new Date().toISOString()

      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'reviews',
          Item: {
            Id: { S: id },
            ProductId: { S: input.productId },
            UserName: { S: input.userName },
            Rating: { N: String(input.rating) },
            Comment: { S: input.comment },
            CreatedAt: { S: createdAt },
            UpdatedAt: { S: updatedAt },
            GSI1PK: { S: input.productId },
            GSI1SK: { S: createdAt },
          },
        }),
      )
      return { id }
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
