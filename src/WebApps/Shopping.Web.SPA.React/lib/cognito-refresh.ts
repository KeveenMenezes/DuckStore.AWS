// Cognito token endpoint calls. Callers live in lib/auth/ only — session state and the decision
// of *when* to refresh belong to lib/auth/session.ts (ADR-0041); this module just talks to Cognito.

export type RefreshedTokens = {
  accessToken: string
  idToken: string
  expiresIn: number
  /**
   * Present only when refresh token rotation is enabled on the user pool client
   * (`refreshTokenRotationGracePeriod`, infra/constructs/appsync-auth.ts). With rotation on, the
   * token used for this exchange is invalidated once the grace period lapses, so this value MUST
   * be persisted — dropping it silently kills the session a few seconds later.
   */
  refreshToken?: string
}

/**
 * Exchanges a refresh token for a new access/id token pair via Cognito's /oauth2/token endpoint.
 * Returns null if the refresh token is expired, revoked, or the user was disabled.
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

  const { access_token, id_token, expires_in, refresh_token } = (await res.json()) as {
    access_token: string
    id_token: string
    expires_in: number
    refresh_token?: string
  }

  return {
    accessToken: access_token,
    idToken: id_token,
    expiresIn: expires_in,
    refreshToken: refresh_token,
  }
}

/**
 * Best-effort revocation at the IdP. The authoritative logout is deleting the session record —
 * this only ensures the refresh token can't be replayed against Cognito directly.
 */
export async function revokeRefreshToken(refreshToken: string): Promise<void> {
  try {
    await fetch(`${process.env.COGNITO_HOSTED_UI_URL}/oauth2/revoke`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        token: refreshToken,
        client_id: process.env.COGNITO_CLIENT_ID!,
      }),
    })
  } catch {
    // Cognito unreachable — the session record is deleted regardless, so access is already gone.
  }
}
