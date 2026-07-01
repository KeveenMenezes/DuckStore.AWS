import { NextResponse } from 'next/server'
import { cookies } from 'next/headers'

/**
 * Returns the authenticated user's identity decoded from the ID Token cookie.
 * The ID Token is signed by Cognito and was validated during the callback flow;
 * we only decode the payload here (no re-verification needed since it came from
 * the server-set httpOnly cookie, not from the client).
 *
 * Returns 401 if no valid session exists.
 */
export async function GET(): Promise<Response> {
  const cookieStore = await cookies()
  const idToken = cookieStore.get('id_token')?.value

  if (!idToken) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
  }

  try {
    const parts = idToken.split('.')
    if (parts.length !== 3) throw new Error('malformed token')
    const payload = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf-8'))

    return NextResponse.json({
      sub: payload.sub as string,
      email: payload.email as string,
      username: (payload['cognito:username'] ?? payload.email) as string,
      groups: (payload['cognito:groups'] as string[] | undefined) ?? [],
    })
  } catch {
    return NextResponse.json({ error: 'Invalid token' }, { status: 401 })
  }
}
