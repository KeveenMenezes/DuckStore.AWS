import { createYoga, createSchema } from 'graphql-yoga'
import { DynamoDBClient, ScanCommand, GetItemCommand } from '@aws-sdk/client-dynamodb'
import { unmarshall } from '@aws-sdk/util-dynamodb'
import { readFileSync } from 'fs'
import { join } from 'path'

const typeDefs = readFileSync(join(process.cwd(), 'graphql/schema.graphql'), 'utf-8')

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

// Returns the Lambda emulator base URL for a given Aspire resource name.
// Aspire injects services__<name>__http__0 for each WithReference() call.
function lambdaUrl(resourceName: string): string {
  const key = `services__${resourceName}__http__0`
  const url = process.env[key]
  if (!url) throw new Error(`Missing Aspire service discovery env var: ${key}`)
  return url
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
          TableName: 'Products',
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
        }
      })

      const nextTokenOut = result.LastEvaluatedKey
        ? Buffer.from(JSON.stringify(result.LastEvaluatedKey)).toString('base64')
        : null

      return { items, nextToken: nextTokenOut }
    },

    async product(_: unknown, { id }: { id: string }) {
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'Products', Key: { Id: { S: id } } }),
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
      }
    },

    async categories(
      _: unknown,
      { pageSize = 20, nextToken }: { pageSize?: number; nextToken?: string },
    ) {
      const result = await dynamoDb.send(
        new ScanCommand({
          TableName: 'Categories',
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

    async basket(_: unknown, { userName }: { userName: string }) {
      const base = lambdaUrl('basket-get-basket')
      const res = await fetch(`${base}/basket/${encodeURIComponent(userName)}`)
      if (res.status === 404) return null
      if (!res.ok) throw new Error(`basket-get-basket error: ${res.status}`)

      // DefaultLambdaJsonSerializer uses PascalCase: { "Cart": { "UserName": ..., "Items": [...] } }
      const body = (await res.json()) as {
        Cart: {
          UserName: string
          Items: Array<{
            Quantity: number
            Color: string
            Price: number
            ProductId: string
            ProductName: string
          }>
          TotalPrice: number
        }
      }

      return {
        userName: body.Cart.UserName,
        items: body.Cart.Items.map(i => ({
          quantity: i.Quantity,
          color: i.Color || null,
          price: Number(i.Price),
          productId: String(i.ProductId),
          productName: i.ProductName,
        })),
        totalPrice: Number(body.Cart.TotalPrice),
      }
    },

    async couponFor(_: unknown, { productName }: { productName: string }) {
      // Read Coupons table directly — mirrors Discount Lambda logic without needing AWS_ENDPOINT_URL_LAMBDA.
      const result = await dynamoDb.send(
        new GetItemCommand({ TableName: 'Coupons', Key: { ProductName: { S: productName } } }),
      )
      if (!result.Item) return { productName, description: 'No Discount', amount: 0 }
      const item = unmarshall(result.Item)
      return {
        productName: item.ProductName as string,
        description: item.Description as string,
        amount: Number(item.Amount),
      }
    },
  },

  Mutation: {
    async storeBasket(
      _: unknown,
      { input }: { input: { userName: string; items: Array<Record<string, unknown>> } },
    ) {
      const base = lambdaUrl('basket-store-basket')
      const body = {
        Cart: {
          UserName: input.userName,
          Items: input.items.map(i => ({
            Quantity: i.quantity,
            Color: (i.color as string) ?? '',
            Price: i.price,
            ProductId: i.productId,
            ProductName: i.productName,
          })),
        },
      }

      const res = await fetch(`${base}/basket`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!res.ok) throw new Error(`storeBasket error: ${res.status}`)

      const data = (await res.json()) as { UserName: string }
      return { userName: data.UserName }
    },

    async checkoutBasket(
      _: unknown,
      { input }: { input: Record<string, unknown> },
    ) {
      const base = lambdaUrl('basket-checkout-basket')
      const body = {
        BasketCheckoutDto: {
          UserName: input.userName,
          CustomerId: input.customerId,
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
      }

      const res = await fetch(`${base}/basket/checkout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!res.ok) throw new Error(`checkoutBasket error: ${res.status}`)

      const data = (await res.json()) as { IsSuccess: boolean }
      return { isSuccess: data.IsSuccess }
    },

    async deleteBasket(_: unknown, { userName }: { userName: string }) {
      const base = lambdaUrl('basket-delete-basket')
      const res = await fetch(`${base}/basket/${encodeURIComponent(userName)}`, {
        method: 'DELETE',
      })
      if (!res.ok) throw new Error(`deleteBasket error: ${res.status}`)
      return { isSuccess: true }
    },
  },
}

const yoga = createYoga({
  schema: createSchema({ typeDefs, resolvers }),
  graphqlEndpoint: "/api/graphql",
});

export async function GET(request: Request) {
  return yoga.handleRequest(request, {});
}

export async function POST(request: Request) {
  return yoga.handleRequest(request, {});
}