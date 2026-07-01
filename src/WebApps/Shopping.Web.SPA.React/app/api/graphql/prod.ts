import { cookies } from 'next/headers'

/**
 * Production GraphQL proxy: reads the Cognito Access Token from the httpOnly
 * cookie set by /api/auth/callback and forwards it to AppSync as Bearer.
 *
 * Unauthenticated requests fall back to the API Key so public catalog queries
 * (products, categories, reviewsByProduct) work without login.
 *
 * The browser never knows the AppSync URL — it always talks to /api/graphql.
 */
export async function handleProd(req: Request): Promise<Response> {
  const cookieStore = await cookies()
  const accessToken = cookieStore.get('access_token')?.value

  const res = await fetch(process.env.APPSYNC_URL!, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken
        ? { Authorization: `Bearer ${accessToken}` }
        : { 'x-api-key': process.env.APPSYNC_API_KEY! }),
    },
    body: await req.text(),
  })

  return new Response(res.body, {
    status: res.status,
    headers: { 'Content-Type': 'application/json' },
  })
}
