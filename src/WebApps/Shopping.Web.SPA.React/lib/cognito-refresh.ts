// Silent refresh of the Cognito session. The access_token/id_token cookies are
// short-lived (1h, see infra/constructs/appsync-auth.ts SpaClient); without this,
// any request after they expire falls through resolveOwner() (lib/identity.ts) to
// a brand-new GUEST# identity, orphaning the signed-in user's cart in DynamoDB.

export const ACCESS_TOKEN_COOKIE = 'access_token'
export const ID_TOKEN_COOKIE = 'id_token'
export const REFRESH_TOKEN_COOKIE = 'refresh_token'
// Matches SpaClient.refreshTokenValidity in infra/constructs/appsync-auth.ts.
export const REFRESH_TOKEN_MAX_AGE_SECONDS = 60 * 60 * 24 * 30

export type RefreshedTokens = {
  accessToken: string
  idToken: string
  expiresIn: number
}

/**
 * Exchanges a refresh token for a new access/id token pair via Cognito's
 * /oauth2/token endpoint. Returns null if the refresh token is expired/revoked.
 */
export async function refreshAccessToken(refreshToken: string): Promise<RefreshedTokens | null> {
  const res = await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'refresh_token',
      client_id: process.env.COGNITO_CLIENT_ID!,
      refresh_token: refreshToken,
    }),
  })

  if (!res.ok) return null

  const { access_token, id_token, expires_in } = (await res.json()) as {
    access_token: string
    id_token: string
    expires_in: number
  }

  return { accessToken: access_token, idToken: id_token, expiresIn: expires_in }
}
