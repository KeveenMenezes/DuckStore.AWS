import { createYoga, createSchema, createGraphQLError } from 'graphql-yoga'
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

// Strip AppSync-only auth directives — graphql-yoga's schema builder doesn't know them.
const typeDefs = readFileSync(join(process.cwd(), 'graphql/schema.graphql'), 'utf-8')
  .replace(/\s*@aws_api_key\b/g, '')
  .replace(/\s*@aws_cognito_user_pools\b/g, '')

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
  Query: {
    async products(
      _: unknown,
      { pageSize = 20, nextToken }: { pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'products',
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
          description: item.Description as string,
          imageUrl: item.ImageUrl as string,
          price: Number(item.Price),
          stock: Number(item.Stock),
          // CategoryIds is a SS type — unmarshall returns a Set; spread to array.
          categoryIds: item.CategoryIds ? [...(item.CategoryIds as Set<string>)] : [],
          averageRating: item.AverageRating ? Number(item.AverageRating) : 0,
          ratingCount: item.RatingCount ? Number(item.RatingCount) : 0,
        }
      })

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    async product(_: unknown, { id }: { id: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'products', Key: { Id: { S: id } } }),
      )
      if (!result.Item) return null
      const item = unmarshall(result.Item)
      return {
        id: item.Id as string,
        name: item.Name as string,
        description: item.Description as string,
        imageUrl: item.ImageUrl as string,
        price: Number(item.Price),
        stock: Number(item.Stock),
        categoryIds: item.CategoryIds ? [...(item.CategoryIds as Set<string>)] : [],
        averageRating: item.AverageRating ? Number(item.AverageRating) : 0,
        ratingCount: item.RatingCount ? Number(item.RatingCount) : 0,
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
        Items: Array<{ Quantity: number; Color: string; Price: number; ProductId: string; ProductName: string }>
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

    async couponFor(_: unknown, { productName }: { productName: string }) {
      // Direct DynamoDB GetItem on the coupons table (ADR-0009: a key lookup needs no Lambda).
      const result = await dynamoDb.send(
        new GetItemCommand({
          TableName: 'coupons',
          Key: { ProductName: { S: productName } },
        }),
      )

      if (!result.Item) {
        return { productName, description: 'No Discount', amount: 0 }
      }

      const coupon = unmarshall(result.Item)
      return {
        productName: coupon.ProductName,
        description: coupon.Description,
        amount: Number(coupon.Amount),
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
      const userId = customerIdFromOwner(context.owner.ownerId)
      const body = await invokeLambda<Record<string, string | undefined>>('user-get-profile', {
        UserId: userId,
        Email: `${userId}@local.dev`,
        Name: userId,
      })
      return {
        userId: body.UserId,
        email: body.Email,
        name: body.Name,
        phone: body.Phone ?? null,
        addressLine: body.AddressLine ?? null,
        city: body.City ?? null,
        state: body.State ?? null,
        zipCode: body.ZipCode ?? null,
        country: body.Country ?? null,
      }
    },
  },

  Mutation: {
    async storeBasket(
      _: unknown,
      { ownerId, input }: { ownerId: string; input: { items: Array<Record<string, unknown>> } },
    ) {
      const body = await invokeLambda<{ OwnerId: string }>('basket-store-basket', {
        Cart: {
          OwnerId: ownerId,
          Items: input.items.map(i => ({
            Quantity: i.quantity,
            Color: (i.color as string) ?? '',
            Price: i.price,
            ProductId: i.productId,
            ProductName: i.productName,
          })),
        },
      })
      return { ownerId: body.OwnerId }
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
          CardName: input.cardName,
          CardNumber: input.cardNumber,
          Expiration: input.expiration,
          Cvv: input.cvv,
          PaymentMethod: input.paymentMethod,
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

    async createProduct(
      _: unknown,
      { input }: { input: Record<string, unknown> },
    ) {
      const id = randomUUID()
      await dynamoDb.send(
        new PutItemCommand({
          TableName: 'products',
          Item: {
            Id: { S: id },
            Name: { S: input.name as string },
            Description: { S: input.description as string },
            ImageUrl: { S: input.imageUrl as string },
            Price: { N: String(input.price) },
            Stock: { N: String(input.stock) },
            CategoryIds: { SS: input.categoryIds as string[] },
          },
        }),
      )
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
              'SET #Name = :name, Description = :desc, ImageUrl = :img, Price = :price, Stock = :stock, CategoryIds = :cats',
            ExpressionAttributeNames: { '#Name': 'Name' },
            ExpressionAttributeValues: {
              ':name': { S: input.name as string },
              ':desc': { S: input.description as string },
              ':img': { S: input.imageUrl as string },
              ':price': { N: String(input.price) },
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

    async createReview(
      _: unknown,
      { input }: { input: { productId: string; userName: string; rating: number; comment: string } },
    ) {
      const id = randomUUID()
      const createdAt = new Date().toISOString()
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
